using System.Text.Json;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.Modules.Training.Domain.Tests;

/// <summary>
/// Validates <c>scripts/programs/akmal-3day.json</c> against the domain rules the API would apply.
/// <para>
/// The seed script talks to the public API, so a bad number in that file surfaces as an HTTP 422
/// with the database already running — which is no help at all while PostgreSQL cannot start here.
/// These tests put the same rules in front of the file without a server: every warm-up percentage,
/// rep range, rest period and working-set count goes through the real value objects.
/// </para>
/// <para>
/// They also pin the two things about that file a reader would want to trust: that it still adds up
/// to the author's 61 working sets a week, and that the bench ramp resolves to the exact weights
/// the integration tests expect.
/// </para>
/// </summary>
public class SeedProgramFileTests
{
    private static readonly SeedProgram Program = LoadSeedProgram();

    private sealed record SeedStep(decimal PercentOfWorking, int Reps, bool IsOptional);

    private sealed record SeedPrescription(
        string Exercise,
        string? Alternative,
        int WorkingSetCount,
        int RepMin,
        int RepMax,
        int RestMinSeconds,
        int RestMaxSeconds,
        List<SeedStep> Warmup,
        string? Notes);

    private sealed record SeedDay(string Label, string? DayOfWeek, List<SeedPrescription> Prescriptions);

    private sealed record SeedProgram(string Name, List<SeedDay> Days);

    private static SeedProgram LoadSeedProgram()
    {
        // Walked up rather than configured: the test project does not copy the file to its output,
        // and hard-coding a relative depth breaks the moment the project moves.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MyGym.sln")))
            directory = directory.Parent;

        directory.ShouldNotBeNull("could not find the repository root (no MyGym.sln above the test output)");

        var path = Path.Combine(directory.FullName, "scripts", "programs", "akmal-3day.json");
        File.Exists(path).ShouldBeTrue($"the seed program file is missing: {path}");

        var program = JsonSerializer.Deserialize<SeedProgram>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        program.ShouldNotBeNull();
        return program;
    }

    private static IEnumerable<SeedPrescription> AllPrescriptions =>
        Program.Days.SelectMany(day => day.Prescriptions);

    [Fact]
    public void The_file_describes_three_days()
    {
        Program.Name.ShouldNotBeNullOrWhiteSpace();
        Program.Days.Count.ShouldBe(3);
        Program.Days.Select(d => d.DayOfWeek).ShouldBe(["Monday", "Wednesday", "Friday"]);
    }

    [Fact]
    public void Every_day_has_at_least_one_prescription()
    {
        // program.day_needs_at_least_one_prescription — an empty day is rejected at activation.
        foreach (var day in Program.Days)
            day.Prescriptions.ShouldNotBeEmpty(day.Label);
    }

    [Fact]
    public void The_weekly_working_set_count_is_still_sixty_one()
    {
        var perDay = Program.Days
            .Select(day => day.Prescriptions.Sum(p => p.WorkingSetCount))
            .ToList();

        // The author's own totals: 21 upper, 20 legs, 20 full body.
        perDay.ShouldBe([21, 20, 20]);
        perDay.Sum().ShouldBe(61);
    }

    [Fact]
    public void Every_rep_range_is_accepted_by_the_domain()
    {
        foreach (var prescription in AllPrescriptions)
        {
            var result = RepRange.Create(prescription.RepMin, prescription.RepMax);
            result.IsSuccess.ShouldBeTrue($"{prescription.Exercise}: {result.Error?.Code}");
        }
    }

    [Fact]
    public void Every_rest_period_is_accepted_by_the_domain()
    {
        foreach (var prescription in AllPrescriptions)
        {
            var result = RestPeriod.Create(
                TimeSpan.FromSeconds(prescription.RestMinSeconds),
                TimeSpan.FromSeconds(prescription.RestMaxSeconds));

            result.IsSuccess.ShouldBeTrue($"{prescription.Exercise}: {result.Error?.Code}");
        }
    }

    [Fact]
    public void Every_working_set_count_is_within_bounds()
    {
        foreach (var prescription in AllPrescriptions)
        {
            prescription.WorkingSetCount.ShouldBeInRange(
                ExercisePrescription.MinWorkingSets,
                ExercisePrescription.MaxWorkingSets,
                prescription.Exercise);
        }
    }

    [Fact]
    public void Every_warmup_step_is_accepted_by_the_domain()
    {
        foreach (var prescription in AllPrescriptions)
        {
            foreach (var step in prescription.Warmup)
            {
                var result = WarmupStep.Create(step.PercentOfWorking, step.Reps, step.IsOptional);
                result.IsSuccess.ShouldBeTrue(
                    $"{prescription.Exercise} @ {step.PercentOfWorking}: {result.Error?.Code}");
            }
        }
    }

    [Fact]
    public void Every_warmup_ramp_ascends_strictly_and_fits_the_step_limit()
    {
        foreach (var prescription in AllPrescriptions)
        {
            var steps = prescription.Warmup
                .Select(s => WarmupStep.Create(s.PercentOfWorking, s.Reps, s.IsOptional).Value)
                .ToList();

            var ramp = WarmupRamp.Create(steps);
            ramp.IsSuccess.ShouldBeTrue($"{prescription.Exercise}: {ramp.Error?.Code}");
        }
    }

    [Fact]
    public void No_percentage_is_written_as_a_whole_number_by_mistake()
    {
        // The single easiest way to get this file wrong: 53 instead of 0.53. The domain rejects it
        // (percentOfWorking must be above 0 and below 1), but the failure would arrive as an HTTP
        // 422 from a running server rather than here.
        foreach (var prescription in AllPrescriptions)
        {
            foreach (var step in prescription.Warmup)
            {
                step.PercentOfWorking.ShouldBeGreaterThan(0m, prescription.Exercise);
                step.PercentOfWorking.ShouldBeLessThan(1m, prescription.Exercise);
            }
        }
    }

    [Fact]
    public void An_alternative_is_never_the_same_exercise_as_the_primary()
    {
        // prescription.alternative_same_as_primary
        foreach (var prescription in AllPrescriptions)
            prescription.Alternative.ShouldNotBe(prescription.Exercise, prescription.Exercise);
    }

    [Fact]
    public void The_bench_press_ramp_resolves_to_the_weights_the_integration_tests_expect()
    {
        var bench = AllPrescriptions.Single(p => p.Exercise == "flat-barbell-bench-press");

        var ramp = WarmupRamp.Create(
            bench.Warmup.Select(s => WarmupStep.Create(s.PercentOfWorking, s.Reps, s.IsOptional).Value)).Value;

        var resolved = ramp.Resolve(
            Weight.Create(80m, WeightUnit.Kilograms).Value,
            WeightIncrement.DefaultKg);

        // 27 / 53 / 80 % of 80 kg, rounded DOWN to 2.5 kg. The same numbers
        // SessionEndpointTests asserts end to end, so the seeded program and the proven
        // critical path describe the same workout. Round-to-nearest would give 22.5 / 42.5 / 65.
        resolved.Select(step => step.Weight.Value).ShouldBe([20m, 40m, 62.5m]);
        resolved.Select(step => step.Reps).ShouldBe([10, 6, 3]);
    }

    [Fact]
    public void Day_labels_are_present_and_distinct()
    {
        // program.day_label_required, plus a guard against a copy-paste that duplicates a day.
        foreach (var day in Program.Days)
            day.Label.ShouldNotBeNullOrWhiteSpace();

        Program.Days.Select(d => d.Label).Distinct().Count().ShouldBe(Program.Days.Count);
    }
}
