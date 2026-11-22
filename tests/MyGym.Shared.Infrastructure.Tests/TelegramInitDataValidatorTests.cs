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

        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = ((authDate ?? Now).ToUnixTimeSeconds()).ToString(),
            ["query_id"] = "AAEtest",
            ["user"] = user
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

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
