using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class SessionEndpointTests(PostgresFixture postgres) : IAsyncLifetime
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

    /// <summary>
    /// The whole point of sub-project 1: author a program, start its day, log a real set, finish,
    /// and come back next week to a suggested working weight drawn from what was actually lifted.
    /// </summary>
    [Fact]
    public async Task The_critical_path_runs_end_to_end()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, programId, dayId) = await CreateActiveProgramAsync();

        // 1. Start the session.
        var started = await client.PostAsJsonAsync(
            "/api/sessions", new { programId, programDayId = dayId, bodyweight = 74.5m }, ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Created);

        var session = (await started.Content.ReadFromJsonAsync<SessionPayload>(ct))!;
        session.Status.ShouldBe("InProgress");
        session.ProgramId.ShouldBe(programId);
        session.ProgramRevision.ShouldNotBeNull();
        session.Exercises.Count.ShouldBe(1);

        var exercise = session.Exercises[0];
        exercise.TargetRepMin.ShouldBe(6);
        exercise.TargetRepMax.ShouldBe(8);
        exercise.WorkingSetCount.ShouldBe(3);
        exercise.SuggestedWarmup.Count.ShouldBe(3);

        // 2. Log warm-ups and working sets.
        foreach (var (weight, reps, kind) in new[]
                 {
                     (20m, 10, "Warmup"), (42.5m, 6, "Warmup"), (62.5m, 3, "Warmup"),
                     (80m, 8, "Working"), (80m, 7, "Working"), (80m, 6, "Working")
                 })
        {
            var logged = await client.PostAsJsonAsync(
                $"/api/sessions/{session.Id}/exercises/{exercise.Id}/sets",
                new { clientSetId = Guid.NewGuid(), kind, weight, reps, rir = 2 }, ct);

            logged.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        // 3. Finish.
        var completed = await client.PostAsync($"/api/sessions/{session.Id}/complete", null, ct);
        completed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var stored = await client.GetFromJsonAsync<SessionPayload>($"/api/sessions/{session.Id}", ct);
        stored!.Status.ShouldBe("Completed");
        stored.WorkingSetCount.ShouldBe(3);
        stored.BodyweightAtSession.ShouldBe(74.5m);
        stored.Exercises[0].Sets.Count.ShouldBe(6);

        // 4. The next preview now suggests the weight actually lifted, with real warm-ups.
        var preview = await client.GetFromJsonAsync<DayPreviewPayload>(
            $"/api/programs/{programId}/days/{dayId}/preview", ct);

        preview!.Exercises[0].SuggestedWorkingWeight.ShouldBe(80m);
        preview.Exercises[0].Warmup.Select(w => w.Weight).ShouldBe([20m, 40m, 62.5m]);
    }

    [Fact]
    public async Task Logging_the_same_client_set_id_twice_creates_one_set()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, programId, dayId) = await CreateActiveProgramAsync();
        var session = await StartAsync(client, programId, dayId);
        var exerciseId = session.Exercises[0].Id;
        var clientSetId = Guid.NewGuid();

        var body = new { clientSetId, kind = "Working", weight = 80m, reps = 8, rir = (int?)2 };

        var first = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{exerciseId}/sets", body, ct);
        var retry = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{exerciseId}/sets", body, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.StatusCode.ShouldBe(HttpStatusCode.Created);

        var firstSet = await first.Content.ReadFromJsonAsync<LoggedSetPayload>(ct);
        var retrySet = await retry.Content.ReadFromJsonAsync<LoggedSetPayload>(ct);
        retrySet!.Id.ShouldBe(firstSet!.Id);

        var stored = await client.GetFromJsonAsync<SessionPayload>($"/api/sessions/{session.Id}", ct);
        stored!.Exercises[0].Sets.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Starting_a_second_session_while_one_is_open_conflicts()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, programId, dayId) = await CreateActiveProgramAsync();
        await StartAsync(client, programId, dayId);

        var second = await client.PostAsJsonAsync(
            "/api/sessions", new { programId, programDayId = dayId, bodyweight = (decimal?)null }, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<ProblemPayload>(ct);
        problem!.ErrorCode.ShouldBe("session.already_in_progress");
    }

    [Fact]
    public async Task The_current_session_is_retrievable_and_absent_once_finished()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, programId, dayId) = await CreateActiveProgramAsync();
        var session = await StartAsync(client, programId, dayId);

        (await client.GetFromJsonAsync<SessionPayload>("/api/sessions/current", ct))!.Id.ShouldBe(session.Id);

        await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{session.Exercises[0].Id}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 80m, reps = 8, rir = (int?)null }, ct);
        await client.PostAsync($"/api/sessions/{session.Id}/complete", null, ct);

        (await client.GetAsync("/api/sessions/current", ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_session_cannot_be_completed_without_a_working_set()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, programId, dayId) = await CreateActiveProgramAsync();
        var session = await StartAsync(client, programId, dayId);

        await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{session.Exercises[0].Id}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Warmup", weight = 20m, reps = 10, rir = (int?)null }, ct);

        var completed = await client.PostAsync($"/api/sessions/{session.Id}/complete", null, ct);

        completed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        var problem = await completed.Content.ReadFromJsonAsync<ProblemPayload>(ct);
        problem!.ErrorCode.ShouldBe("session.needs_at_least_one_working_set");
    }

    [Fact]
    public async Task A_closed_session_rejects_further_sets()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, programId, dayId) = await CreateActiveProgramAsync();
        var session = await StartAsync(client, programId, dayId);
        var exerciseId = session.Exercises[0].Id;

        await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{exerciseId}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 80m, reps = 8, rir = (int?)null }, ct);
        await client.PostAsync($"/api/sessions/{session.Id}/complete", null, ct);

        var late = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{exerciseId}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 85m, reps = 5, rir = (int?)null }, ct);

        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await late.Content.ReadFromJsonAsync<ProblemPayload>(ct))!.ErrorCode.ShouldBe("session.closed");
    }

    [Fact]
    public async Task Another_athlete_cannot_touch_someone_elses_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, programId, dayId) = await CreateActiveProgramAsync();
        var session = await StartAsync(owner, programId, dayId);

        var stranger = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        (await stranger.GetAsync($"/api/sessions/{session.Id}", ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        var write = await stranger.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{session.Exercises[0].Id}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 80m, reps = 8, rir = (int?)null }, ct);

        write.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_abandoned_session_frees_the_athlete_to_start_another()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, programId, dayId) = await CreateActiveProgramAsync();
        var session = await StartAsync(client, programId, dayId);

        (await client.PostAsync($"/api/sessions/{session.Id}/abandon", null, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var second = await client.PostAsJsonAsync(
            "/api/sessions", new { programId, programDayId = dayId, bodyweight = (decimal?)null }, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Session_history_lists_completed_sessions_newest_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, programId, dayId) = await CreateActiveProgramAsync();

        for (var i = 0; i < 2; i++)
        {
            var session = await StartAsync(client, programId, dayId);
            await client.PostAsJsonAsync(
                $"/api/sessions/{session.Id}/exercises/{session.Exercises[0].Id}/sets",
                new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 80m + i, reps = 8, rir = (int?)null },
                ct);
            await client.PostAsync($"/api/sessions/{session.Id}/complete", null, ct);
        }

        var history = await client.GetFromJsonAsync<List<SessionSummaryPayload>>("/api/sessions", ct);

        history!.Count.ShouldBe(2);
        history[0].CompletedAt!.Value.ShouldBeGreaterThanOrEqualTo(history[1].CompletedAt!.Value);
    }

    [Fact]
    public async Task An_ad_hoc_session_accepts_exercises_added_mid_workout()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        Guid curlId;
        await using (var catalog = postgres.CreateCatalogContext())
            curlId = (await catalog.Exercises.SingleAsync(e => e.Slug == "ez-bar-curl", ct)).Id.Value;

        var started = await client.PostAsJsonAsync("/api/sessions",
            new { programId = (Guid?)null, programDayId = (Guid?)null, bodyweight = (decimal?)null }, ct);

        started.StatusCode.ShouldBe(HttpStatusCode.Created);
        var session = (await started.Content.ReadFromJsonAsync<SessionPayload>(ct))!;
        session.ProgramId.ShouldBeNull();
        session.Exercises.ShouldBeEmpty();

        var added = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/exercises", new
        {
            exerciseId = curlId,
            workingSetCount = 2,
            repMin = 10,
            repMax = 12,
            restMinSeconds = 60,
            restMaxSeconds = 120
        }, ct);

        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        var exercise = (await added.Content.ReadFromJsonAsync<AddedExercisePayload>(ct))!;

        var logged = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{exercise.SessionExerciseId}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 25m, reps = 11, rir = (int?)1 }, ct);

        logged.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await client.PostAsync($"/api/sessions/{session.Id}/complete", null, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<SessionPayload> StartAsync(HttpClient client, Guid programId, Guid dayId)
    {
        var ct = TestContext.Current.CancellationToken;
        var started = await client.PostAsJsonAsync(
            "/api/sessions", new { programId, programDayId = dayId, bodyweight = (decimal?)null }, ct);
        started.EnsureSuccessStatusCode();
        return (await started.Content.ReadFromJsonAsync<SessionPayload>(ct))!;
    }

    private async Task<(HttpClient Client, Guid ProgramId, Guid DayId)> CreateActiveProgramAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        await using var catalog = postgres.CreateCatalogContext();
        var bench = await catalog.Exercises.SingleAsync(e => e.Slug == "flat-barbell-bench-press", ct);

        var created = await client.PostAsJsonAsync("/api/programs", new
        {
            name = "Critical path program",
            days = new[]
            {
                new
                {
                    label = "Juma — Full Body",
                    dayOfWeek = (string?)null,
                    prescriptions = new[]
                    {
                        new
                        {
                            exerciseId = bench.Id.Value,
                            alternativeExerciseId = (Guid?)null,
                            workingSetCount = 3,
                            repMin = 6,
                            repMax = 8,
                            restMinSeconds = 150,
                            restMaxSeconds = 180,
                            warmup = new[]
                            {
                                new { percentOfWorking = 0.27m, reps = 10, isOptional = false },
                                new { percentOfWorking = 0.53m, reps = 6, isOptional = false },
                                new { percentOfWorking = 0.80m, reps = 3, isOptional = false }
                            },
                            notes = (string?)null
                        }
                    }
                }
            }
        }, ct);

        created.EnsureSuccessStatusCode();
        var program = (await created.Content.ReadFromJsonAsync<ProgramPayload>(ct))!;
        await client.PostAsync($"/api/programs/{program.Id}/activate", null, ct);

        return (client, program.Id, program.Days[0].Id);
    }

    internal sealed record ProgramPayload(Guid Id, List<DayPayload> Days);
    internal sealed record DayPayload(Guid Id);

    internal sealed record SessionPayload(
        Guid Id, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
        Guid? ProgramId, int? ProgramRevision, Guid? ProgramDayId, decimal? BodyweightAtSession,
        int WorkingSetCount, List<SessionExercisePayload> Exercises);

    internal sealed record SessionExercisePayload(
        Guid Id, int Order, Guid ExerciseId, int WorkingSetCount, int TargetRepMin, int TargetRepMax,
        List<ResolvedWarmupPayload> SuggestedWarmup, decimal? SuggestedWorkingWeight,
        List<LoggedSetPayload> Sets);

    internal sealed record LoggedSetPayload(
        Guid Id, int Order, string Kind, decimal Weight, int Reps, int? Rir, Guid ClientSetId);

    internal sealed record AddedExercisePayload(Guid SessionExerciseId);

    internal sealed record ResolvedWarmupPayload(decimal Weight, int Reps, bool IsOptional);

    internal sealed record DayPreviewPayload(List<PreviewExercisePayload> Exercises);

    internal sealed record PreviewExercisePayload(
        decimal? SuggestedWorkingWeight, List<ResolvedWarmupPayload> Warmup);

    internal sealed record SessionSummaryPayload(
        Guid Id, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int WorkingSetCount);

    internal sealed record ProblemPayload(string Title, int Status, string ErrorCode);
}
