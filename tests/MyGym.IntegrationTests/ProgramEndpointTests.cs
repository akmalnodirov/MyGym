using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class ProgramEndpointTests(PostgresFixture postgres) : IAsyncLifetime
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

    private async Task<(HttpClient Client, Guid BenchId, Guid RowId)> SetupAsync()
    {
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        await using var catalog = postgres.CreateCatalogContext();
        var bench = await catalog.Exercises.SingleAsync(e => e.Slug == "flat-barbell-bench-press");
        var row = await catalog.Exercises.SingleAsync(e => e.Slug == "seated-cable-row");

        return (client, bench.Id.Value, row.Id.Value);
    }

    private static object BuildProgramBody(Guid benchId, Guid rowId) =>
        new { name = "3 Kunlik Muscle Gain", days = BuildDays(benchId, rowId) };

    private static object[] BuildDays(Guid benchId, Guid rowId) =>
        new object[]
        {
            new
            {
                label = "Juma — Full Body",
                dayOfWeek = "Friday",
                prescriptions = new object[]
                {
                    new
                    {
                        exerciseId = benchId,
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
                    },
                    new
                    {
                        exerciseId = rowId,
                        alternativeExerciseId = (Guid?)null,
                        workingSetCount = 3,
                        repMin = 8,
                        repMax = 10,
                        restMinSeconds = 180,
                        restMaxSeconds = 180,
                        warmup = new[]
                        {
                            new { percentOfWorking = 0.55m, reps = 8, isOptional = false }
                        },
                        notes = (string?)null
                    }
                }
            }
        };

    [Fact]
    public async Task A_program_is_created_as_a_draft_and_read_back_in_full()
    {
        var (client, benchId, rowId) = await SetupAsync();

        var created = await client.PostAsJsonAsync(
            "/api/programs", BuildProgramBody(benchId, rowId), TestContext.Current.CancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var program = await created.Content.ReadFromJsonAsync<ProgramPayload>(
            TestContext.Current.CancellationToken);
        program!.Status.ShouldBe("Draft");
        program.RevisionNumber.ShouldBeGreaterThan(0);
        program.Days.ShouldHaveSingleItem();
        program.Days[0].Prescriptions.Count.ShouldBe(2);
        program.Days[0].Prescriptions[0].Warmup.Count.ShouldBe(3);

        var fetched = await client.GetFromJsonAsync<ProgramPayload>(
            $"/api/programs/{program.Id}", TestContext.Current.CancellationToken);
        fetched!.Name.ShouldBe("3 Kunlik Muscle Gain");
    }

    [Fact]
    public async Task An_unknown_exercise_id_is_rejected()
    {
        var (client, _, rowId) = await SetupAsync();

        var response = await client.PostAsJsonAsync(
            "/api/programs", BuildProgramBody(Guid.CreateVersion7(), rowId),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(
            TestContext.Current.CancellationToken);
        problem!.ErrorCode.ShouldBe("exercise.not_found");
    }

    [Fact]
    public async Task A_descending_warmup_ramp_is_rejected_with_its_domain_error_code()
    {
        var (client, benchId, rowId) = await SetupAsync();
        var body = new
        {
            name = "Bad ramp",
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
                            exerciseId = benchId,
                            alternativeExerciseId = (Guid?)null,
                            workingSetCount = 3,
                            repMin = 6,
                            repMax = 8,
                            restMinSeconds = 180,
                            restMaxSeconds = 180,
                            warmup = new[]
                            {
                                new { percentOfWorking = 0.80m, reps = 3, isOptional = false },
                                new { percentOfWorking = 0.50m, reps = 8, isOptional = false }
                            },
                            notes = (string?)null
                        }
                    }
                }
            }
        };

        var response = await client.PostAsJsonAsync(
            "/api/programs", body, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(
            TestContext.Current.CancellationToken);
        problem!.ErrorCode.ShouldBe("warmup.percent_not_ascending");
    }

    [Fact]
    public async Task Activating_a_program_succeeds_and_a_second_activation_conflicts()
    {
        var (client, benchId, rowId) = await SetupAsync();
        var program = await CreateAsync(client, benchId, rowId);

        (await client.PostAsync(
                $"/api/programs/{program.Id}/activate", null, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsync(
                $"/api/programs/{program.Id}/activate", null, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Replacing_a_program_bumps_its_revision()
    {
        var (client, benchId, rowId) = await SetupAsync();
        var program = await CreateAsync(client, benchId, rowId);

        var replaced = await client.PutAsJsonAsync(
            $"/api/programs/{program.Id}",
            new { name = "Renamed block", days = BuildDays(benchId, rowId) },
            TestContext.Current.CancellationToken);

        replaced.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await replaced.Content.ReadFromJsonAsync<ProgramPayload>(
            TestContext.Current.CancellationToken);
        updated!.Name.ShouldBe("Renamed block");
        updated.RevisionNumber.ShouldBeGreaterThan(program.RevisionNumber);
    }

    [Fact]
    public async Task Another_athlete_cannot_read_a_private_program()
    {
        var (owner, benchId, rowId) = await SetupAsync();
        var program = await CreateAsync(owner, benchId, rowId);

        var stranger = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        (await stranger.GetAsync(
                $"/api/programs/{program.Id}", TestContext.Current.CancellationToken)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_public_program_can_be_forked_by_another_athlete()
    {
        var (owner, benchId, rowId) = await SetupAsync();
        var program = await CreateAsync(owner, benchId, rowId);
        await owner.PostAsync(
            $"/api/programs/{program.Id}/activate", null, TestContext.Current.CancellationToken);
        await owner.PatchAsJsonAsync(
            $"/api/programs/{program.Id}/visibility", new { visibility = "Public" },
            TestContext.Current.CancellationToken);

        var stranger = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));
        var forked = await stranger.PostAsync(
            $"/api/programs/{program.Id}/fork", null, TestContext.Current.CancellationToken);

        forked.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fork = await forked.Content.ReadFromJsonAsync<ProgramPayload>(TestContext.Current.CancellationToken);
        fork!.Id.ShouldNotBe(program.Id);
        fork.Status.ShouldBe("Draft");
        fork.Days[0].Prescriptions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_day_preview_resolves_every_warmup_to_a_loadable_weight()
    {
        var (client, benchId, rowId) = await SetupAsync();
        var program = await CreateAsync(client, benchId, rowId);
        var dayId = program.Days[0].Id;

        var preview = await client.GetFromJsonAsync<DayPreviewPayload>(
            $"/api/programs/{program.Id}/days/{dayId}/preview", TestContext.Current.CancellationToken);

        preview!.Exercises.Count.ShouldBe(2);
        var bench = preview.Exercises[0];
        bench.TargetRepMin.ShouldBe(6);
        bench.TargetRepMax.ShouldBe(8);
        bench.WorkingSetCount.ShouldBe(3);
        bench.Warmup.Count.ShouldBe(3);

        // No history yet, so there is no suggested working weight and warm-ups resolve to zero.
        bench.SuggestedWorkingWeight.ShouldBeNull();
        bench.Warmup.ShouldAllBe(step => step.Weight == 0m);

        // Every resolved weight must sit on the athlete's 2.5 kg increment.
        foreach (var step in bench.Warmup)
            (step.Weight % 2.5m).ShouldBe(0m);
    }

    [Fact]
    public async Task The_catalog_can_be_searched_in_uzbek()
    {
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        var results = await client.GetFromJsonAsync<List<ExercisePayload>>(
            "/api/exercises?q=shtanga", TestContext.Current.CancellationToken);

        results!.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Listing_programs_returns_my_own_and_flags_ownership()
    {
        var (client, benchId, rowId) = await SetupAsync();
        var mine = await CreateAsync(client, benchId, rowId);

        var programs = await client.GetFromJsonAsync<List<ProgramSummaryPayload>>(
            "/api/programs", TestContext.Current.CancellationToken);

        programs!.ShouldContain(p => p.Id == mine.Id && p.IsOwnedByMe && p.DayCount == 1);
    }

    [Fact]
    public async Task Listing_programs_does_not_leak_another_athletes_private_program()
    {
        var (owner, benchId, rowId) = await SetupAsync();
        var hidden = await CreateAsync(owner, benchId, rowId);

        var stranger = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));
        var programs = await stranger.GetFromJsonAsync<List<ProgramSummaryPayload>>(
            "/api/programs", TestContext.Current.CancellationToken);

        programs!.ShouldNotContain(p => p.Id == hidden.Id);
    }

    private async Task<ProgramPayload> CreateAsync(HttpClient client, Guid benchId, Guid rowId)
    {
        var created = await client.PostAsJsonAsync("/api/programs", BuildProgramBody(benchId, rowId));
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<ProgramPayload>())!;
    }

    internal sealed record ProgramPayload(
        Guid Id, string Name, string Status, string Visibility, int RevisionNumber,
        List<ProgramDayPayload> Days);

    internal sealed record ProgramSummaryPayload(
        Guid Id, string Name, string Status, string Visibility, int DayCount, bool IsOwnedByMe);

    internal sealed record ProgramDayPayload(
        Guid Id, int Order, string Label, List<PrescriptionPayload> Prescriptions);

    internal sealed record PrescriptionPayload(
        Guid Id, int Order, Guid ExerciseId, int WorkingSetCount, int RepMin, int RepMax,
        List<WarmupPayload> Warmup);

    internal sealed record WarmupPayload(decimal PercentOfWorking, int Reps, bool IsOptional);

    internal sealed record DayPreviewPayload(Guid DayId, string Label, List<PreviewExercisePayload> Exercises);

    internal sealed record PreviewExercisePayload(
        Guid ExerciseId, int WorkingSetCount, int TargetRepMin, int TargetRepMax,
        decimal? SuggestedWorkingWeight, List<ResolvedWarmupPayload> Warmup);

    internal sealed record ResolvedWarmupPayload(decimal Weight, int Reps, bool IsOptional);

    internal sealed record ExercisePayload(Guid Id, string Slug, string Name);

    internal sealed record ProblemPayload(string Title, int Status, string ErrorCode);
}
