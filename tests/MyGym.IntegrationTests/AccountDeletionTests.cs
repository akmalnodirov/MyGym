using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class AccountDeletionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFixture _api = null!;

    public async ValueTask InitializeAsync()
    {
        _api = new ApiFixture(postgres);
        await using var catalog = postgres.CreateCatalogContext();
        await MyGym.Modules.ExerciseCatalog.Infrastructure.CatalogSeeder.SeedAsync(
            catalog, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Deleting_an_account_removes_the_user_and_all_training_data()
    {
        var ct = TestContext.Current.CancellationToken;
        var telegramId = Random.Shared.NextInt64(1, long.MaxValue);
        var client = await _api.CreateAuthenticatedClientAsync(telegramId);
        var me = await client.GetFromJsonAsync<MePayload>("/api/me", ct);

        await using var catalog = postgres.CreateCatalogContext();
        var bench = await catalog.Exercises.SingleAsync(e => e.Slug == "flat-barbell-bench-press", ct);

        var program = await client.PostAsJsonAsync("/api/programs", new
        {
            name = "To be deleted",
            days = new[]
            {
                new
                {
                    label = "Day",
                    dayOfWeek = (string?)null,
                    prescriptions = new[]
                    {
                        new
                        {
                            exerciseId = bench.Id.Value,
                            alternativeExerciseId = (Guid?)null,
                            workingSetCount = 3, repMin = 6, repMax = 8,
                            restMinSeconds = 180, restMaxSeconds = 180,
                            warmup = Array.Empty<object>(),
                            notes = (string?)null
                        }
                    }
                }
            }
        }, ct);
        program.EnsureSuccessStatusCode();

        var deleted = await client.DeleteAsync("/api/me", ct);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var identity = postgres.CreateIdentityContext();
        (await identity.Users.AnyAsync(u => u.TelegramUserId == telegramId, ct)).ShouldBeFalse();
        (await identity.RefreshTokens.CountAsync(t => t.RevokedAt == null
            && t.UserId == MyGym.SharedKernel.UserId.From(me!.Id), ct)).ShouldBe(0);

        await using var training = postgres.CreateTrainingContext();
        (await training.Programs.CountAsync(
            p => p.OwnerUserId == MyGym.SharedKernel.UserId.From(me!.Id), ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Tokens_stop_working_after_deletion()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));
        await client.DeleteAsync("/api/me", ct);

        // The access token is still cryptographically valid until it expires, but the account is
        // gone, so the profile lookup must fail rather than resurrect anything.
        (await client.GetAsync("/api/me", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_deleted_athlete_can_sign_up_again_as_a_new_account()
    {
        var ct = TestContext.Current.CancellationToken;
        var telegramId = Random.Shared.NextInt64(1, long.MaxValue);
        var first = await _api.CreateAuthenticatedClientAsync(telegramId);
        var firstMe = await first.GetFromJsonAsync<MePayload>("/api/me", ct);
        await first.DeleteAsync("/api/me", ct);

        var second = await _api.CreateAuthenticatedClientAsync(telegramId);
        var secondMe = await second.GetFromJsonAsync<MePayload>("/api/me", ct);

        secondMe!.Id.ShouldNotBe(firstMe!.Id);
    }

    internal sealed record MePayload(Guid Id);
}
