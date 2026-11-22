using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class ProfileEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFixture _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFixture(postgres);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task A_profile_can_be_updated_and_read_back()
    {
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        var update = await client.PatchAsJsonAsync("/api/me", new
        {
            birthYear = 1996,
            bodyweight = 74.5m,
            preferredUnit = "Kilograms",
            loadingIncrement = 2.5m,
            experienceLevel = "Intermediate",
            availableDaysPerWeek = 3,
            sessionMinutes = 90,
            timeZoneId = "Asia/Tashkent"
        }, TestContext.Current.CancellationToken);

        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        var profile = await client.GetFromJsonAsync<ProfilePayload>(
            "/api/me", TestContext.Current.CancellationToken);
        profile!.BirthYear.ShouldBe(1996);
        profile.Bodyweight.ShouldBe(74.5m);
        profile.AvailableDaysPerWeek.ShouldBe(3);
        profile.TimeZoneId.ShouldBe("Asia/Tashkent");
    }

    [Fact]
    public async Task An_invalid_profile_is_rejected_with_its_domain_error_code()
    {
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        var response = await client.PatchAsJsonAsync("/api/me", new
        {
            birthYear = 1850,
            preferredUnit = "Kilograms",
            loadingIncrement = 2.5m,
            experienceLevel = "Beginner",
            timeZoneId = "UTC"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(
            TestContext.Current.CancellationToken);
        problem!.ErrorCode.ShouldBe("profile.birth_year_out_of_bounds");
    }

    internal sealed record ProfilePayload(
        Guid Id, string FirstName, int? BirthYear, decimal? Bodyweight,
        int? AvailableDaysPerWeek, int? SessionMinutes, string TimeZoneId);

    internal sealed record ProblemPayload(string Title, int Status, string ErrorCode);
}
