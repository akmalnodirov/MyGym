using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MyGym.Shared.Infrastructure.Auth;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace MyGym.Api.Endpoints;

public static class TelegramWebhookEndpoints
{
    public const string SecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";
    public const string WebhookPath = "/api/telegram/webhook";

    /// <summary>
    /// I12: the secret-token check used to run inside the handler, after the `Update` parameter was
    /// already model-bound — which happens before any endpoint filter can run (filters execute
    /// after parameter binding, not before it), so only real middleware placed ahead of routing
    /// actually runs early enough. Without this, an unauthenticated caller forced up to the
    /// endpoint's request-size-limit worth of JSON deserialisation on every request, and malformed
    /// JSON returned a framework 400 instead of 401. Branched with UseWhen so it only runs for the
    /// webhook path and rejoins the main pipeline for everything else.
    /// </summary>
    public static IApplicationBuilder UseTelegramWebhookSecretGuard(this IApplicationBuilder app) =>
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(WebhookPath),
            branch => branch.Use(async (context, next) =>
            {
                var telegram = context.RequestServices.GetRequiredService<IOptions<TelegramAuthOptions>>();
                var presented = context.Request.Headers[SecretTokenHeader].ToString();

                if (!SecretTokensMatch(presented, telegram.Value.WebhookSecretToken))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                await next(context);
            }));

    public static void MapTelegramWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost(WebhookPath, async (
                Update update,
                IOptions<MiniAppOptions> miniApp,
                ITelegramBotClient bot,
                CancellationToken ct) =>
            {
                if (update.Message?.Text is "/start" && update.Message.Chat.Id is var chatId)
                {
                    await bot.SendMessage(
                        chatId: chatId,
                        text: "Salom! Mashg'ulotlarni yozib borish uchun ilovani ochadi.",
                        replyMarkup: new InlineKeyboardMarkup(
                            InlineKeyboardButton.WithWebApp("MyGym", new WebAppInfo { Url = miniApp.Value.Url })),
                        cancellationToken: ct);
                }

                // Always 200: Telegram retries anything else, and a retry storm is worse than a
                // silently ignored update type we do not handle yet.
                return Results.Ok();
            })
            .AllowAnonymous()
            .WithMetadata(new RequestSizeLimitAttribute(64 * 1024));
    }

    /// <summary>
    /// Constant-time comparison of the presented header value against the configured webhook
    /// secret. <c>internal</c> rather than <c>private</c> so it can be unit-tested directly from
    /// MyGym.Api.Tests without standing up a host — see
    /// MyGym.Api.Tests/WebhookSecretTokenMatcherTests.cs and this project's AssemblyInfo.cs, which
    /// grants MyGym.Api.Tests InternalsVisibleTo access.
    /// </summary>
    internal static bool SecretTokensMatch(string presented, string expected)
    {
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        // A length mismatch is decided before FixedTimeEquals rather than inside it — comparing
        // two different-length byte spans for equality isn't meaningful in the first place, and the
        // length of a webhook secret isn't itself a secret worth constant-time-protecting. A
        // missing header becomes an empty string here, which only ever matches an empty configured
        // secret — and AddValidatedAppSettings' eager validation guarantees WebhookSecretToken is
        // never blank, so a missing header always falls through to this branch and is rejected.
        return presentedBytes.Length == expectedBytes.Length
               && CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes);
    }
}

public sealed class MiniAppOptions
{
    public const string SectionName = "MiniApp";

    public required string Url { get; init; }
}
