using MyGym.Api.Endpoints;
using Shouldly;

namespace MyGym.Api.Tests;

/// <summary>
/// TelegramWebhookEndpoints.SecretTokensMatch is a pure function of two strings: no host, no HTTP
/// request, no Telegram call. It is also the one piece of the webhook that must get constant-time
/// comparison and the missing-header case right, so it is worth pinning down directly rather than
/// trusting it only through the 401/200 integration tests.
/// </summary>
public class WebhookSecretTokenMatcherTests
{
    [Fact]
    public void Matching_tokens_are_accepted()
    {
        TelegramWebhookEndpoints.SecretTokensMatch("test-webhook-secret", "test-webhook-secret")
            .ShouldBeTrue();
    }

    [Fact]
    public void A_different_token_of_the_same_length_is_rejected()
    {
        TelegramWebhookEndpoints.SecretTokensMatch("test-webhook-secre1", "test-webhook-secre2")
            .ShouldBeFalse();
    }

    [Fact]
    public void A_token_of_a_different_length_is_rejected_without_throwing()
    {
        TelegramWebhookEndpoints.SecretTokensMatch("short", "test-webhook-secret").ShouldBeFalse();
    }

    [Fact]
    public void A_missing_header_presented_as_empty_is_rejected_rather_than_matched()
    {
        // HttpContext yields "" for a header that was never sent, via StringValues.ToString().
        // That must never be treated as an empty-string match against a configured secret.
        TelegramWebhookEndpoints.SecretTokensMatch("", "test-webhook-secret").ShouldBeFalse();
    }

    [Fact]
    public void Two_empty_strings_are_still_a_length_match()
    {
        // Documents the boundary rather than relying on it: AddValidatedAppSettings' eager
        // ValidateOnStart guarantees the configured secret is never blank in a running app, so this
        // case can't occur against real configuration — but the function itself has no opinion on
        // that, it only compares bytes.
        TelegramWebhookEndpoints.SecretTokensMatch("", "").ShouldBeTrue();
    }
}
