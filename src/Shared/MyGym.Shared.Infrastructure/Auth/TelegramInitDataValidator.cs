using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MyGym.SharedKernel;

namespace MyGym.Shared.Infrastructure.Auth;

public sealed record TelegramUserInfo(
    long TelegramUserId,
    string FirstName,
    string? Username,
    string? LanguageCode,
    DateTimeOffset AuthDate);

public interface ITelegramInitDataValidator
{
    Result<TelegramUserInfo> Validate(string initData);
}

/// <summary>
/// Verifies the signature Telegram attaches to Mini App init data.
/// See https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app
/// </summary>
public sealed class TelegramInitDataValidator(
    IOptions<TelegramAuthOptions> options,
    TimeProvider timeProvider) : ITelegramInitDataValidator
{
    private const string SecretKeySalt = "WebAppData";

    // DateTimeOffset.FromUnixTimeSeconds throws ArgumentOutOfRangeException outside this range
    // (roughly year 1 to year 9999). long.TryParse happily accepts long.MaxValue/MinValue, so
    // nothing upstream of this filters it — it must be checked before converting.
    private static readonly long MinAuthDateUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long MaxAuthDateUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    public Result<TelegramUserInfo> Validate(string initData)
    {
        if (string.IsNullOrWhiteSpace(initData))
            return Fail(AuthErrors.InitDataMalformed, "Init data is empty.");

        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
                return Fail(AuthErrors.InitDataMalformed, "Init data is not a query string.");

            fields[pair[..separator]] = WebUtility.UrlDecode(pair[(separator + 1)..]);
        }

        if (!fields.Remove("hash", out var providedHash) || providedHash.Length != 64)
            return Fail(AuthErrors.InitDataMalformed, "Init data has no usable hash.");

        if (!fields.TryGetValue("auth_date", out var authDateRaw)
            || !long.TryParse(authDateRaw, out var authDateUnix))
            return Fail(AuthErrors.InitDataMalformed, "Init data has no auth_date.");

        if (!fields.TryGetValue("user", out var userJson))
            return Fail(AuthErrors.InitDataMalformed, "Init data has no user.");

        var dataCheckString = string.Join('\n', fields.Select(f => $"{f.Key}={f.Value}"));
        if (!SignatureMatches(dataCheckString, providedHash))
            return Fail(AuthErrors.InitDataSignatureInvalid, "Init data signature does not match.");

        if (authDateUnix < MinAuthDateUnixSeconds || authDateUnix > MaxAuthDateUnixSeconds)
            return Fail(AuthErrors.InitDataMalformed, "Init data auth_date is out of range.");

        var authDate = DateTimeOffset.FromUnixTimeSeconds(authDateUnix);
        var now = timeProvider.GetUtcNow();
        if (authDate > now.AddMinutes(1) || now - authDate > options.Value.MaxInitDataAge)
            return Fail(AuthErrors.InitDataExpired, "Init data is outside the accepted time window.");

        return ParseUser(userJson, authDate);
    }

    private bool SignatureMatches(string dataCheckString, string providedHash)
    {
        // providedHash is attacker-controlled and only checked for length (64) before reaching
        // here. A 64-character string that is not valid hex (e.g. non-hex letters) makes
        // Convert.FromHexString throw FormatException. That must not become an unhandled
        // exception on an authentication path — it is simply an invalid signature.
        byte[] provided;
        try
        {
            provided = Convert.FromHexString(providedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        using var secretKeyHmac = new HMACSHA256(Encoding.UTF8.GetBytes(SecretKeySalt));
        var secretKey = secretKeyHmac.ComputeHash(Encoding.UTF8.GetBytes(options.Value.BotToken));

        using var signatureHmac = new HMACSHA256(secretKey);
        var computed = signatureHmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));

        return CryptographicOperations.FixedTimeEquals(computed, provided);
    }

    private static Result<TelegramUserInfo> ParseUser(string userJson, DateTimeOffset authDate)
    {
        try
        {
            using var document = JsonDocument.Parse(userJson);
            var root = document.RootElement;

            // user is signed, so it is not attacker-controlled in the forgery sense — but it is
            // still untrusted shape: valid JSON that is not an object (null, an array, a bare
            // number or boolean) makes TryGetProperty throw InvalidOperationException rather than
            // returning false. A protocol change on Telegram's side must fail cleanly, not crash
            // every sign-in.
            if (root.ValueKind != JsonValueKind.Object)
                return Fail(AuthErrors.InitDataMalformed, "User payload is not a JSON object.");

            if (!root.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.Number
                || !idElement.TryGetInt64(out var id))
                return Fail(AuthErrors.InitDataMalformed, "User payload has no numeric id.");

            if (!TryGetStringProperty(root, "first_name", isOptional: false, out var firstName)
                || string.IsNullOrWhiteSpace(firstName))
                return Fail(AuthErrors.InitDataMalformed, "User payload has no first_name.");

            if (!TryGetStringProperty(root, "username", isOptional: true, out var username))
                return Fail(AuthErrors.InitDataMalformed, "User payload has a non-string username.");

            if (!TryGetStringProperty(root, "language_code", isOptional: true, out var languageCode))
                return Fail(AuthErrors.InitDataMalformed, "User payload has a non-string language_code.");

            return Result<TelegramUserInfo>.Success(new TelegramUserInfo(
                id, firstName!, username, languageCode, authDate));
        }
        catch (JsonException)
        {
            return Fail(AuthErrors.InitDataMalformed, "User payload is not valid JSON.");
        }
    }

    /// <summary>
    /// Reads a string-typed property safely. A missing property resolves to <c>null</c> only when
    /// <paramref name="isOptional"/> is true (and likewise for an explicit JSON null, since
    /// <see cref="JsonElement.GetString"/> already treats null safely) — a required property that
    /// is missing, null, or present with a non-string value (e.g. a number or boolean) fails.
    /// </summary>
    private static bool TryGetStringProperty(
        JsonElement root, string propertyName, bool isOptional, out string? value)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            value = null;
            return isOptional;
        }

        if (element.ValueKind == JsonValueKind.Null && isOptional)
        {
            value = null;
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            value = null;
            return false;
        }

        value = element.GetString();
        return true;
    }

    private static Result<TelegramUserInfo> Fail(string code, string message) =>
        Result<TelegramUserInfo>.Failure(new Error(code, message));
}
