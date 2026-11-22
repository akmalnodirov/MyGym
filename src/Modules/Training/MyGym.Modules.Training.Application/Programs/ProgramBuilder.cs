using MyGym.Modules.ExerciseCatalog.Contracts;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Programs;

/// <summary>
/// Turns request input into a validated aggregate. Shared by create and replace so the two paths
/// cannot drift apart. <see cref="ApplyPrescription"/> and <see cref="EnsureExercisesExistAsync"/>
/// are internal (not private) specifically so <c>ReplaceProgramCommandHandler</c> can call them
/// instead of re-implementing the same validation inline.
/// </summary>
internal static class ProgramBuilder
{
    public static async Task<Result<TrainingProgram>> BuildAsync(
        UserId owner,
        string name,
        IReadOnlyList<ProgramDayInput> days,
        IExerciseCatalogQueries catalog,
        CancellationToken ct)
    {
        var exercisesExist = await EnsureExercisesExistAsync(days, catalog, ct);
        if (exercisesExist.IsFailure)
            return Result<TrainingProgram>.Failure(exercisesExist.Error);

        var created = TrainingProgram.Create(owner, name);
        if (created.IsFailure)
            return created;

        var program = created.Value;

        foreach (var dayInput in days)
        {
            var day = program.AddDay(dayInput.Label, dayInput.DayOfWeek);
            if (day.IsFailure)
                return Result<TrainingProgram>.Failure(day.Error);

            foreach (var input in dayInput.Prescriptions)
            {
                var applied = ApplyPrescription(program, day.Value.Id, input);
                if (applied.IsFailure)
                    return Result<TrainingProgram>.Failure(applied.Error);
            }
        }

        return Result<TrainingProgram>.Success(program);
    }

    /// <summary>
    /// Validates that every exercise (and alternative) referenced across a set of day inputs
    /// exists in the catalog. Shared by create and replace so a payload that slips a bad id past
    /// one path cannot slip it past the other.
    /// </summary>
    internal static async Task<Result> EnsureExercisesExistAsync(
        IReadOnlyList<ProgramDayInput> days, IExerciseCatalogQueries catalog, CancellationToken ct)
    {
        var exerciseIds = days
            .SelectMany(d => d.Prescriptions)
            .SelectMany(p => p.AlternativeExerciseId is { } alt
                ? new[] { ExerciseId.From(p.ExerciseId), ExerciseId.From(alt) }
                : [ExerciseId.From(p.ExerciseId)])
            .ToList();

        if (!await catalog.AllExistAsync(exerciseIds, ct))
            return Result.Failure(new Error(
                ExerciseCatalogErrors.ExerciseNotFound, "One or more exercises do not exist."));

        return Result.Success();
    }

    /// <summary>Validates and adds one prescription to an already-added day. Internal so it is
    /// reusable from <c>ReplaceProgramCommandHandler</c> without duplicating the rep-range/rest/
    /// warm-up-ramp construction logic.</summary>
    internal static Result ApplyPrescription(
        TrainingProgram program, ProgramDayId dayId, PrescriptionInput input)
    {
        var reps = RepRange.Create(input.RepMin, input.RepMax);
        if (reps.IsFailure)
            return Result.Failure(reps.Error);

        var rest = RestPeriod.Create(
            TimeSpan.FromSeconds(input.RestMinSeconds), TimeSpan.FromSeconds(input.RestMaxSeconds));
        if (rest.IsFailure)
            return Result.Failure(rest.Error);

        var steps = new List<WarmupStep>();
        foreach (var stepInput in input.Warmup)
        {
            var step = WarmupStep.Create(stepInput.PercentOfWorking, stepInput.Reps, stepInput.IsOptional);
            if (step.IsFailure)
                return Result.Failure(step.Error);

            steps.Add(step.Value);
        }

        var ramp = WarmupRamp.Create(steps);
        if (ramp.IsFailure)
            return Result.Failure(ramp.Error);

        var added = program.AddPrescription(
            dayId,
            ExerciseId.From(input.ExerciseId),
            input.AlternativeExerciseId is { } alt ? ExerciseId.From(alt) : null,
            input.WorkingSetCount, reps.Value, rest.Value, ramp.Value, input.Notes);

        return added.IsFailure ? Result.Failure(added.Error) : Result.Success();
    }
}
