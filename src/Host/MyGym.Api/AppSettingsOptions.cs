using System.Text;
using MyGym.Shared.Infrastructure.Auth;

namespace MyGym.Api;

/// <summary>
/// Eager validation for the options that, if misconfigured, would otherwise let the app boot
/// healthy and then fail on the first real request. A signing key shorter than 32 bytes makes
/// HS256 token creation throw (IDX10653) the first time a token is issued, not at startup — so
/// every option here is validated with <c>ValidateOnStart</c>, which runs during host startup and
/// refuses to boot a misconfigured deployment rather than let it fail silently later.
/// </summary>
public static class AppSettingsOptions
{
    public static IServiceCollection AddValidatedAppSettings(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TelegramAuthOptions>()
            .Bind(configuration.GetSection(TelegramAuthOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.BotToken),
                "Telegram:BotToken is required.")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.WebhookSecretToken),
                "Telegram:WebhookSecretToken is required.")
            .ValidateOnStart();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Issuer),
                "Jwt:Issuer is required.")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Audience),
                "Jwt:Audience is required.")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.SigningKey)
                     && Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
                "Jwt:SigningKey must be present and at least 32 bytes (UTF-8) long — HS256 token " +
                "creation throws IDX10653 on the first request with anything shorter.")
            .ValidateOnStart();

        return services;
    }
}
