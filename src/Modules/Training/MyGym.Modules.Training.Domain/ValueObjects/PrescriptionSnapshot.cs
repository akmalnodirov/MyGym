using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.ValueObjects;

/// <summary>
/// What was prescribed, frozen at the moment a session started. Programs may be edited afterwards;
/// a logged session must never change as a result.
/// </summary>
public sealed class PrescriptionSnapshot : ValueObject
{
    private PrescriptionSnapshot(
        ExerciseId exerciseId,
        ExerciseId? alternativeExerciseId,
        int workingSetCount,
        RepRange targetReps,
        RestPeriod rest,
        WarmupRamp warmup,
        string? notes)
    {
        ExerciseId = exerciseId;
        AlternativeExerciseId = alternativeExerciseId;
        WorkingSetCount = workingSetCount;
        TargetReps = targetReps;
        Rest = rest;
        Warmup = warmup;
        Notes = notes;
    }

    public ExerciseId ExerciseId { get; }
    public ExerciseId? AlternativeExerciseId { get; }
    public int WorkingSetCount { get; }
    public RepRange TargetReps { get; }
    public RestPeriod Rest { get; }
    public WarmupRamp Warmup { get; }
    public string? Notes { get; }

    public static Result<PrescriptionSnapshot> Create(
        ExerciseId exerciseId,
        ExerciseId? alternativeExerciseId,
        int workingSetCount,
        RepRange targetReps,
        RestPeriod rest,
        WarmupRamp warmup,
        string? notes)
    {
        if (workingSetCount is < 1 or > 10)
            return Result<PrescriptionSnapshot>.Failure(new Error(
                TrainingErrors.PrescriptionWorkingSetsOutOfBounds,
                "Working sets must be between 1 and 10."));

        return Result<PrescriptionSnapshot>.Success(new PrescriptionSnapshot(
            exerciseId, alternativeExerciseId, workingSetCount, targetReps, rest, warmup, notes));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ExerciseId;
        yield return AlternativeExerciseId;
        yield return WorkingSetCount;
        yield return TargetReps;
        yield return Rest;
        yield return Warmup;
        yield return Notes;
    }
}
