using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Programs;

public sealed class ProgramDay : Entity<ProgramDayId>
{
    private readonly List<ExercisePrescription> _prescriptions = [];

    private ProgramDay(ProgramDayId id, ProgramId programId, int order, string label, DayOfWeek? dayOfWeek)
        : base(id)
    {
        ProgramId = programId;
        Order = order;
        Label = label;
        DayOfWeek = dayOfWeek;
    }

    public ProgramId ProgramId { get; private set; }
    public int Order { get; private set; }
    public string Label { get; private set; }
    public DayOfWeek? DayOfWeek { get; private set; }

    public IReadOnlyList<ExercisePrescription> Prescriptions =>
        _prescriptions.OrderBy(p => p.Order).ToList();

    internal static ProgramDay Create(ProgramId programId, int order, string label, DayOfWeek? dayOfWeek) =>
        new(ProgramDayId.New(), programId, order, label.Trim(), dayOfWeek);

    internal void SetOrder(int order) => Order = order;

    internal Result<ExercisePrescription> AddPrescription(
        ExerciseId exerciseId,
        ExerciseId? alternativeExerciseId,
        int workingSetCount,
        RepRange targetReps,
        RestPeriod rest,
        WarmupRamp warmup,
        string? notes)
    {
        var result = ExercisePrescription.Create(
            Id, _prescriptions.Count + 1, exerciseId, alternativeExerciseId,
            workingSetCount, targetReps, rest, warmup, notes);

        if (result.IsFailure)
            return result;

        _prescriptions.Add(result.Value);
        return result;
    }

    internal ProgramDay CopyTo(ProgramId newProgramId)
    {
        var copy = new ProgramDay(ProgramDayId.New(), newProgramId, Order, Label, DayOfWeek);
        copy._prescriptions.AddRange(Prescriptions.Select(p => p.CopyTo(copy.Id)));
        return copy;
    }
}
