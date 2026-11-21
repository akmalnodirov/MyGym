using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Programs;

public sealed class ExercisePrescription : Entity<PrescriptionId>
{
    public const int MinWorkingSets = 1;
    public const int MaxWorkingSets = 10;

    private ExercisePrescription(
        PrescriptionId id,
        ProgramDayId programDayId,
        int order,
        ExerciseId exerciseId,
        ExerciseId? alternativeExerciseId,
        int workingSetCount,
        RepRange targetReps,
        RestPeriod rest,
        WarmupRamp warmup,
        string? notes) : base(id)
    {
        ProgramDayId = programDayId;
        Order = order;
        ExerciseId = exerciseId;
        AlternativeExerciseId = alternativeExerciseId;
        WorkingSetCount = workingSetCount;
        TargetReps = targetReps;
        Rest = rest;
        Warmup = warmup;
        Notes = notes;
    }

    public ProgramDayId ProgramDayId { get; private set; }
    public int Order { get; private set; }
    public ExerciseId ExerciseId { get; private set; }
    public ExerciseId? AlternativeExerciseId { get; private set; }
    public int WorkingSetCount { get; private set; }
    public RepRange TargetReps { get; private set; }
    public RestPeriod Rest { get; private set; }
    public WarmupRamp Warmup { get; private set; }
    public string? Notes { get; private set; }

    internal static Result<ExercisePrescription> Create(
        ProgramDayId programDayId,
        int order,
        ExerciseId exerciseId,
        ExerciseId? alternativeExerciseId,
        int workingSetCount,
        RepRange targetReps,
        RestPeriod rest,
        WarmupRamp warmup,
        string? notes)
    {
        if (workingSetCount is < MinWorkingSets or > MaxWorkingSets)
            return Result<ExercisePrescription>.Failure(new Error(
                TrainingErrors.PrescriptionWorkingSetsOutOfBounds,
                $"Working sets must be between {MinWorkingSets} and {MaxWorkingSets}."));

        if (alternativeExerciseId is not null && alternativeExerciseId == exerciseId)
            return Result<ExercisePrescription>.Failure(new Error(
                TrainingErrors.PrescriptionAlternativeSameAsPrimary,
                "The alternative exercise must differ from the primary exercise."));

        return Result<ExercisePrescription>.Success(new ExercisePrescription(
            PrescriptionId.New(), programDayId, order, exerciseId, alternativeExerciseId,
            workingSetCount, targetReps, rest, warmup, notes));
    }

    internal ExercisePrescription CopyTo(ProgramDayId newDayId) => new(
        PrescriptionId.New(), newDayId, Order, ExerciseId, AlternativeExerciseId,
        WorkingSetCount, TargetReps, Rest, Warmup, Notes);
}
