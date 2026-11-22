using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Telegram.Bot.Requests;
using Telegram.Bot.Types.ReplyMarkups;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class WebhookTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFixture _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFixture(postgres);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private static object StartUpdate() => new
    {
        update_id = 1,
        message = new
        {
            message_id = 1,
            date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            chat = new { id = 555L, type = "private" },
            from = new { id = 555L, is_bot = false, first_name = "Akmal" },
            text = "/start"
        }
    };

    [Fact]
    public async Task A_webhook_request_without_the_secret_token_is_rejected()
    {
        var response = await _api.CreateClient().PostAsJsonAsync(
            "/api/telegram/webhook", StartUpdate(), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_webhook_request_with_a_wrong_secret_token_is_rejected()
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Telegram-Bot-Api-Secret-Token", "wrong");

        var response = await client.PostAsJsonAsync(
            "/api/telegram/webhook", StartUpdate(), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_valid_start_update_sends_a_mini_app_button()
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Telegram-Bot-Api-Secret-Token", "test-webhook-secret");

        var response = await client.PostAsJsonAsync(
            "/api/telegram/webhook", StartUpdate(), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Unrun-tests finding #4: asserting only the status code let this pass even with the
        // entire "/start" handling block deleted — FakeTelegramBotClient recorded nothing and
        // nothing about the response depended on whether a message was actually sent. Assert the
        // bot's one real side effect instead: a SendMessage carrying a WebAppInfo button pointed
        // at the configured HTTPS Mini App URL (appsettings.json's MiniApp:Url, which ApiFixture
        // does not override).
        var sent = _api.TelegramBot.SentRequests.OfType<SendMessageRequest>().ShouldHaveSingleItem();
        sent.ChatId.Identifier.ShouldBe(555L);

        var markup = sent.ReplyMarkup.ShouldBeOfType<InlineKeyboardMarkup>();
        var button = markup.InlineKeyboard.SelectMany(row => row).ShouldHaveSingleItem();

        button.WebApp.ShouldNotBeNull();
        button.WebApp!.Url.ShouldBe("https://localhost:5001");
    }
}
