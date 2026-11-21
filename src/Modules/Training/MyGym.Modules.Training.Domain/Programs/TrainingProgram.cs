using MyGym.Modules.Training.Contracts;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Programs;

public enum ProgramStatus
{
    Draft = 1,
    Active = 2,
    Archived = 3
}

public enum ProgramVisibility
{
    Private = 1,
    Public = 2
}

public sealed class TrainingProgram : AggregateRoot<ProgramId>
{
    public const int MaxNameLength = 120;

    private readonly List<ProgramDay> _days = [];

    private TrainingProgram(ProgramId id, UserId ownerUserId, string name, ProgramId? forkedFrom)
        : base(id)
    {
        OwnerUserId = ownerUserId;
        Name = name;
        Status = ProgramStatus.Draft;
        Visibility = ProgramVisibility.Private;
        ForkedFromProgramId = forkedFrom;
        RevisionNumber = 1;
    }

    public UserId OwnerUserId { get; private set; }
    public string Name { get; private set; }
    public ProgramStatus Status { get; private set; }
    public ProgramVisibility Visibility { get; private set; }
    public ProgramId? ForkedFromProgramId { get; private set; }
    public int RevisionNumber { get; private set; }

    public IReadOnlyList<ProgramDay> Days => _days.OrderBy(d => d.Order).ToList();

    public static Result<TrainingProgram> Create(UserId ownerUserId, string name)
    {
        var validated = ValidateName(name);
        return validated.IsFailure
            ? Result<TrainingProgram>.Failure(validated.Error)
            : Result<TrainingProgram>.Success(
                new TrainingProgram(ProgramId.New(), ownerUserId, validated.Value, forkedFrom: null));
    }

    public Result Rename(string name)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable;

        var validated = ValidateName(name);
        if (validated.IsFailure)
            return Result.Failure(validated.Error);

        Name = validated.Value;
        RevisionNumber++;
        return Result.Success();
    }

    public Result<ProgramDay> AddDay(string label, DayOfWeek? dayOfWeek)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return Result<ProgramDay>.Failure(editable.Error);

        if (string.IsNullOrWhiteSpace(label))
            return Result<ProgramDay>.Failure(
                new Error(TrainingErrors.ProgramNameRequired, "A day needs a label."));

        var day = ProgramDay.Create(Id, _days.Count + 1, label, dayOfWeek);
        _days.Add(day);
        RevisionNumber++;
        return Result<ProgramDay>.Success(day);
    }

    public Result RemoveDay(ProgramDayId dayId)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable;

        var day = _days.SingleOrDefault(d => d.Id == dayId);
        if (day is null)
            return Result.Failure(new Error(TrainingErrors.ProgramDayNotFound, "Day not found."));

        _days.Remove(day);
        Renumber();
        RevisionNumber++;
        return Result.Success();
    }

    public Result<ExercisePrescription> AddPrescription(
        ProgramDayId dayId,
        ExerciseId exerciseId,
        ExerciseId? alternativeExerciseId,
        int workingSetCount,
        RepRange targetReps,
        RestPeriod rest,
        WarmupRamp warmup,
        string? notes)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return Result<ExercisePrescription>.Failure(editable.Error);

        var day = _days.SingleOrDefault(d => d.Id == dayId);
        if (day is null)
            return Result<ExercisePrescription>.Failure(
                new Error(TrainingErrors.ProgramDayNotFound, "Day not found."));

        var result = day.AddPrescription(
            exerciseId, alternativeExerciseId, workingSetCount, targetReps, rest, warmup, notes);

        if (result.IsSuccess)
            RevisionNumber++;

        return result;
    }

    public Result Activate()
    {
        if (Status == ProgramStatus.Archived)
            return Result.Failure(new Error(TrainingErrors.ProgramArchived, "Program is archived."));

        if (Status == ProgramStatus.Active)
            return Result.Failure(new Error(TrainingErrors.ProgramAlreadyActive, "Program is already active."));

        if (_days.Count == 0)
            return Result.Failure(new Error(TrainingErrors.ProgramNeedsDay, "A program needs at least one day."));

        if (_days.Any(d => d.Prescriptions.Count == 0))
            return Result.Failure(new Error(
                TrainingErrors.ProgramDayNeedsPrescription, "Every day needs at least one exercise."));

        Status = ProgramStatus.Active;
        Raise(new ProgramActivated(Id, OwnerUserId, RevisionNumber));
        return Result.Success();
    }

    public Result Archive()
    {
        if (Status == ProgramStatus.Archived)
            return Result.Failure(new Error(TrainingErrors.ProgramArchived, "Program is already archived."));

        Status = ProgramStatus.Archived;
        return Result.Success();
    }

    public Result SetVisibility(ProgramVisibility visibility)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable;

        Visibility = visibility;
        return Result.Success();
    }

    public TrainingProgram Fork(UserId newOwnerUserId)
    {
        var fork = new TrainingProgram(ProgramId.New(), newOwnerUserId, Name, forkedFrom: Id);
        fork._days.AddRange(Days.Select(d => d.CopyTo(fork.Id)));
        return fork;
    }

    private Result EnsureEditable() =>
        Status == ProgramStatus.Archived
            ? Result.Failure(new Error(TrainingErrors.ProgramNotEditable, "An archived program cannot be edited."))
            : Result.Success();

    private void Renumber()
    {
        var order = 1;
        foreach (var day in _days.OrderBy(d => d.Order).ToList())
            day.SetOrder(order++);
    }

    private static Result<string> ValidateName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > MaxNameLength
            ? Result<string>.Failure(new Error(
                TrainingErrors.ProgramNameRequired,
                $"A program name is required and must be at most {MaxNameLength} characters."))
            : Result<string>.Success(trimmed);
    }
}
