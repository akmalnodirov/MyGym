namespace MyGym.Shared.Infrastructure.Auth;

public sealed class TelegramAuthOptions
{
    public const string SectionName = "Telegram";

    public required string BotToken { get; init; }
    public required string WebhookSecretToken { get; init; }
    public TimeSpan MaxInitDataAge { get; init; } = TimeSpan.FromHours(1);
}

public static class AuthErrors
{
    public const string InitDataMalformed = "auth.init_data_malformed";
    public const string InitDataSignatureInvalid = "auth.init_data_signature_invalid";
    public const string InitDataExpired = "auth.init_data_expired";
    public const string RefreshTokenInvalid = "auth.refresh_token_invalid";
}
