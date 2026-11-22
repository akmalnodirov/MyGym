using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests.Abstractions;

namespace MyGym.IntegrationTests;

/// <summary>
/// A no-op <see cref="ITelegramBotClient"/> for the test host, replacing the real client that
/// Program.cs registers with ApiFixture's fake bot token. Without this, the webhook's handling of
/// "/start" would call Telegram.Bot's SendMessage extension method, which calls through to
/// SendRequest below and would otherwise make a genuine HTTPS call to api.telegram.org — a call
/// Telegram would reject outright given a fabricated token, turning the unhandled
/// ApiRequestException into a 500 and failing WebhookTests' expectation of 200. Every member below
/// is a deliberate no-op so no test ever touches the network or depends on Telegram being up.
/// </summary>
public sealed class FakeTelegramBotClient : ITelegramBotClient
{
    public bool LocalBotServer => false;
    public long BotId => 0;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(1);
    public IExceptionParser ExceptionsParser { get; set; } = new DefaultExceptionParser();

    /// <summary>
    /// Unrun-tests finding #4: WebhookTests.A_valid_start_update_is_accepted used to assert only
    /// the HTTP status code, which still passes even if the entire "/start" handling block were
    /// deleted — this fake recorded nothing and the status code never depended on whether a
    /// message was actually sent. Every request the webhook makes through this fake is captured
    /// here so a test can assert on what was actually sent, not just that the endpoint returned
    /// 200.
    /// </summary>
    public List<object> SentRequests { get; } = [];

#pragma warning disable CS0067 // Required by the interface; this fake never raises either event.
    public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest;
    public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived;
#pragma warning restore CS0067

    public Task<TResponse> SendRequest<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        SentRequests.Add(request);
        return Task.FromResult(default(TResponse)!);
    }

#pragma warning disable CS0618 // Obsolete members of ITelegramBotClient must still be implemented.
    public Task<TResponse> MakeRequest<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        Task.FromResult(default(TResponse)!);

    public Task<TResponse> MakeRequestAsync<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        Task.FromResult(default(TResponse)!);
#pragma warning restore CS0618

    public Task<bool> TestApi(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task DownloadFile(
        string filePath, Stream destination, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
