using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class AuthEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFixture _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFixture(postgres);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Valid_init_data_issues_tokens_and_registers_the_user_on_first_sight()
    {
        var telegramId = Random.Shared.NextInt64(1, long.MaxValue);
        var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/telegram", new { initData = ApiFixture.BuildInitData(telegramId) },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await response.Content.ReadFromJsonAsync<ApiFixture.AuthTokens>(
            TestContext.Current.CancellationToken);
        tokens!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        tokens.AccessTokenExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Signing_in_twice_reuses_the_same_account()
    {
        var telegramId = Random.Shared.NextInt64(1, long.MaxValue);

        var first = await _api.CreateAuthenticatedClientAsync(telegramId);
        var firstMe = await first.GetFromJsonAsync<ProfilePayload>(
            "/api/me", TestContext.Current.CancellationToken);

        var second = await _api.CreateAuthenticatedClientAsync(telegramId);
        var secondMe = await second.GetFromJsonAsync<ProfilePayload>(
            "/api/me", TestContext.Current.CancellationToken);

        secondMe!.Id.ShouldBe(firstMe!.Id);
    }

    [Fact]
    public async Task Tampered_init_data_is_rejected_with_a_stable_error_code()
    {
        var client = _api.CreateClient();
        var honest = ApiFixture.BuildInitData(Random.Shared.NextInt64(1, long.MaxValue));
        var forged = honest.Replace("hash=", "hash=0") [..^1];

        var response = await client.PostAsJsonAsync(
            "/api/auth/telegram", new { initData = forged }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(
            TestContext.Current.CancellationToken);
        problem!.ErrorCode.ShouldStartWith("auth.");
    }

    [Fact]
    public async Task A_refresh_token_exchanges_for_a_new_access_token()
    {
        var client = _api.CreateClient();
        var signIn = await client.PostAsJsonAsync(
            "/api/auth/telegram",
            new { initData = ApiFixture.BuildInitData(Random.Shared.NextInt64(1, long.MaxValue)) },
            TestContext.Current.CancellationToken);
        var tokens = await signIn.Content.ReadFromJsonAsync<ApiFixture.AuthTokens>(
            TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = tokens!.RefreshToken },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refreshed = await response.Content.ReadFromJsonAsync<ApiFixture.AuthTokens>(
            TestContext.Current.CancellationToken);
        refreshed!.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_rejected()
    {
        var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = "not-a-real-token" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_endpoints_require_a_token()
    {
        var response = await _api.CreateClient().GetAsync(
            "/api/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    internal sealed record ProfilePayload(Guid Id, string FirstName, string TimeZoneId);
    internal sealed record ProblemPayload(string Title, int Status, string ErrorCode);
}
