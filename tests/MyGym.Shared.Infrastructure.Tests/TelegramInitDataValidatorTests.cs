using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MyGym.Shared.Infrastructure.Auth;
using Shouldly;

namespace MyGym.Shared.Infrastructure.Tests;

public class TelegramInitDataValidatorTests
{
    private const string BotToken = "123456:TEST-BOT-TOKEN-FOR-UNIT-TESTS";
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static TelegramInitDataValidator CreateValidator() =>
        new(Options.Create(new TelegramAuthOptions
        {
            BotToken = BotToken,
            WebhookSecretToken = "irrelevant-here",
            MaxInitDataAge = TimeSpan.FromHours(1)
        }), new FakeTimeProvider(Now));

    /// <summary>
    /// Builds initData the way Telegram does. This mirrors the algorithm under test, so it cannot
    /// prove interoperability with Telegram — only the tamper, staleness, and parsing tests below
    /// have that power, and the true end-to-end check is opening the real Mini App (Task 14).
    /// </summary>
    private static string BuildInitData(
        long userId = 123456789L,
        string firstName = "Akmal",
        string? username = "akmal",
        string? languageCode = "uz",
        DateTimeOffset? authDate = null,
        string? overrideHash = null)
    {
        var user = $"{{\"id\":{userId},\"first_name\":\"{firstName}\"" +
                   (username is null ? "" : $",\"username\":\"{username}\"") +
                   (languageCode is null ? "" : $",\"language_code\":\"{languageCode}\"") + "}";

        return SignAndEncode(user, (authDate ?? Now).ToUnixTimeSeconds().ToString(), overrideHash);
    }

    /// <summary>
    /// Like <see cref="BuildInitData"/>, but lets a test supply an arbitrary raw <c>user</c> JSON
    /// payload and/or raw <c>auth_date</c> string, correctly signed. This is how the odd-shaped
    /// payloads below (non-object user, non-numeric id, out-of-range auth_date, ...) get past the
    /// signature check — a real client that controls the bot token's secret can produce exactly
    /// these shapes, so the validator must fail them cleanly rather than throw.
    /// </summary>
    private static string BuildInitDataWithRawUser(string userJson, string? authDateRaw = null) =>
        SignAndEncode(userJson, authDateRaw ?? Now.ToUnixTimeSeconds().ToString(), overrideHash: null);

    private static string SignAndEncode(string userJson, string authDateRaw, string? overrideHash)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = authDateRaw,
            ["query_id"] = "AAEtest",
            ["user"] = userJson
        };

        var dataCheckString = string.Join('\n', fields.Select(f => $"{f.Key}={f.Value}"));
        using var secretKeyHmac = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secretKey = secretKeyHmac.ComputeHash(Encoding.UTF8.GetBytes(BotToken));
        using var signatureHmac = new HMACSHA256(secretKey);
        var hash = Convert.ToHexStringLower(
            signatureHmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString)));

        var parts = fields.Select(f => $"{f.Key}={WebUtility.UrlEncode(f.Value)}").ToList();
        parts.Add($"hash={overrideHash ?? hash}");
        return string.Join('&', parts);
    }

    [Fact]
    public void Valid_init_data_yields_the_telegram_user()
    {
        var result = CreateValidator().Validate(BuildInitData());

        result.IsSuccess.ShouldBeTrue(result.Error.Code);
        result.Value.TelegramUserId.ShouldBe(123456789L);
        result.Value.FirstName.ShouldBe("Akmal");
        result.Value.Username.ShouldBe("akmal");
        result.Value.LanguageCode.ShouldBe("uz");
        result.Value.AuthDate.ShouldBe(Now);
    }

    [Fact]
    public void An_optional_username_and_language_may_be_absent()
    {
        var result = CreateValidator().Validate(BuildInitData(username: null, languageCode: null));

        result.IsSuccess.ShouldBeTrue(result.Error.Code);
        result.Value.Username.ShouldBeNull();
        result.Value.LanguageCode.ShouldBeNull();
    }

    [Fact]
    public void A_tampered_hash_is_rejected()
    {
        var result = CreateValidator().Validate(
            BuildInitData(overrideHash: new string('a', 64)));

        result.Error.Code.ShouldBe(AuthErrors.InitDataSignatureInvalid);
    }

    [Fact]
    public void A_tampered_payload_is_rejected()
    {
        // Keep the signature from user 123456789 but claim to be someone else.
        var honest = BuildInitData();
        var hash = honest.Split("hash=")[1];
        var forged = BuildInitData(userId: 999999999L, overrideHash: hash);

        CreateValidator().Validate(forged).Error.Code.ShouldBe(AuthErrors.InitDataSignatureInvalid);
    }

    [Fact]
    public void Init_data_older_than_the_configured_window_is_rejected()
    {
        var result = CreateValidator().Validate(BuildInitData(authDate: Now.AddHours(-2)));

        result.Error.Code.ShouldBe(AuthErrors.InitDataExpired);
    }

    [Fact]
    public void Init_data_from_the_future_is_rejected()
    {
        CreateValidator().Validate(BuildInitData(authDate: Now.AddMinutes(10)))
            .Error.Code.ShouldBe(AuthErrors.InitDataExpired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("auth_date=1&user=%7B%7D")]
    [InlineData("hash=abc")]
    [InlineData("not-a-query-string")]
    public void Malformed_init_data_is_rejected(string initData)
    {
        CreateValidator().Validate(initData).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_64_character_non_hex_hash_is_rejected_cleanly_rather_than_throwing()
    {
        // The length check alone would accept this (64 chars), but it is not valid hex, so
        // Convert.FromHexString would throw if the validator did not guard against it.
        var result = CreateValidator().Validate(
            BuildInitData(overrideHash: new string('z', 64)));

        result.Error.Code.ShouldBe(AuthErrors.InitDataSignatureInvalid);
    }

    // The payloads below are validly signed (BuildInitDataWithRawUser computes a correct
    // signature over whatever raw user/auth_date is supplied), so they reach ParseUser and the
    // post-signature auth_date conversion. None of these are forgeries - they model Telegram
    // sending a shape the validator does not expect. No exception may escape Validate; every case
    // must resolve to a clean auth.init_data_malformed failure instead of a 500.

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("true")]
    public void A_signed_but_non_object_user_payload_is_rejected_cleanly(string userJson)
    {
        var result = CreateValidator().Validate(BuildInitDataWithRawUser(userJson));

        result.Error.Code.ShouldBe(AuthErrors.InitDataMalformed);
    }

    [Fact]
    public void A_signed_user_with_a_string_id_is_rejected_cleanly()
    {
        var result = CreateValidator().Validate(
            BuildInitDataWithRawUser("{\"id\":\"123456789\",\"first_name\":\"Akmal\"}"));

        result.Error.Code.ShouldBe(AuthErrors.InitDataMalformed);
    }

    [Theory]
    [InlineData("{\"id\":123456789,\"first_name\":42}")]
    [InlineData("{\"id\":123456789,\"first_name\":true}")]
    public void A_signed_user_with_a_non_string_first_name_is_rejected_cleanly(string userJson)
    {
        var result = CreateValidator().Validate(BuildInitDataWithRawUser(userJson));

        result.Error.Code.ShouldBe(AuthErrors.InitDataMalformed);
    }

    [Theory]
    [InlineData("{\"id\":123456789,\"first_name\":\"Akmal\",\"username\":42}")]
    [InlineData("{\"id\":123456789,\"first_name\":\"Akmal\",\"language_code\":42}")]
    public void A_signed_user_with_a_non_string_optional_field_is_rejected_cleanly(string userJson)
    {
        var result = CreateValidator().Validate(BuildInitDataWithRawUser(userJson));

        result.Error.Code.ShouldBe(AuthErrors.InitDataMalformed);
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void A_signed_but_out_of_range_auth_date_is_rejected_cleanly(long authDateUnix)
    {
        const string validUser = "{\"id\":123456789,\"first_name\":\"Akmal\"}";
        var result = CreateValidator().Validate(
            BuildInitDataWithRawUser(validUser, authDateUnix.ToString()));

        result.Error.Code.ShouldBe(AuthErrors.InitDataMalformed);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
