using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;
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
        var createdProgram = (await program.Content.ReadFromJsonAsync<ProgramPayload>(ct))!;
        await client.PostAsync($"/api/programs/{createdProgram.Id}/activate", null, ct);

        // Unrun-tests finding #1: the original version of this test only ever created a
        // *program*. The purge's first ExecuteDeleteAsync — sessions, and the cascade into
        // session_exercises and logged_sets that TrainingDataPurger's own comment relies on — ran
        // against an empty table, so nothing would have noticed if that cascade were missing.
        // Starting a session and logging a real set exercises all four tables spec §10's "all
        // training data" actually claims to clear, not just one of them.
        var started = await client.PostAsJsonAsync("/api/sessions", new
        {
            programId = createdProgram.Id,
            programDayId = createdProgram.Days[0].Id,
            bodyweight = (decimal?)null
        }, ct);
        started.EnsureSuccessStatusCode();
        var session = (await started.Content.ReadFromJsonAsync<SessionPayload>(ct))!;

        var loggedSet = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{session.Exercises[0].Id}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 80m, reps = 8, rir = (int?)null }, ct);
        loggedSet.EnsureSuccessStatusCode();

        var deleted = await client.DeleteAsync("/api/me", ct);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var userId = UserId.From(me!.Id);

        await using var identity = postgres.CreateIdentityContext();
        (await identity.Users.AnyAsync(u => u.TelegramUserId == telegramId, ct)).ShouldBeFalse();

        // I10: a real hard delete, not a revoke — the old assertion (RevokedAt == null) would
        // still pass if the rows were merely revoked and left in place forever.
        (await identity.RefreshTokens.CountAsync(t => t.UserId == userId, ct)).ShouldBe(0);

        await using var training = postgres.CreateTrainingContext();
        (await training.Programs.CountAsync(p => p.OwnerUserId == userId, ct)).ShouldBe(0);
        (await training.Sessions.CountAsync(s => s.UserId == userId, ct)).ShouldBe(0);

        var sessionId = SessionId.From(session.Id);
        (await training.Set<SessionExercise>().CountAsync(e => e.SessionId == sessionId, ct)).ShouldBe(0);

        var sessionExerciseId = SessionExerciseId.From(session.Exercises[0].Id);
        (await training.Set<LoggedSet>().CountAsync(s => s.SessionExerciseId == sessionExerciseId, ct))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Tokens_stop_working_after_deletion()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));
        await client.DeleteAsync("/api/me", ct);

        // The access token is still cryptographically valid until it expires, but the account is
        // gone, so every write path must fail rather than resurrect anything.
        (await client.GetAsync("/api/me", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // C3 reproduction (Phase 4 test #3, unrun-tests finding #2): the original version of this
        // test asserted only GET /api/me, which overclaims "tokens stop working" — that endpoint
        // merely happens to look the user up directly. Before the fix, both calls below succeeded
        // and inserted training rows keyed to a user_id identity.users no longer had, because a
        // null profile lookup silently substituted default preferences instead of failing.
        var program = await client.PostAsJsonAsync("/api/programs", new
        {
            name = "Should not be created",
            days = new[] { new { label = "Day", dayOfWeek = (string?)null, prescriptions = Array.Empty<object>() } }
        }, ct);
        program.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var session = await client.PostAsJsonAsync(
            "/api/sessions",
            new { programId = (Guid?)null, programDayId = (Guid?)null, bodyweight = (decimal?)null }, ct);
        session.StatusCode.ShouldBe(HttpStatusCode.NotFound);
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

    internal sealed record ProgramPayload(Guid Id, List<ProgramDayPayload> Days);
    internal sealed record ProgramDayPayload(Guid Id);

    internal sealed record SessionPayload(Guid Id, List<SessionExercisePayload> Exercises);
    internal sealed record SessionExercisePayload(Guid Id);
}
