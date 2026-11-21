using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Sessions;

public enum SetKind
{
    Warmup = 1,
    Working = 2
}

public sealed class LoggedSet : Entity<LoggedSetId>
{
    public const int MinReps = 0;
    public const int MaxReps = 100;
    public const int MinRir = 0;
    public const int MaxRir = 10;

    private LoggedSet(
        LoggedSetId id,
        SessionExerciseId sessionExerciseId,
        int order,
        SetKind kind,
        Weight weight,
        int reps,
        int? rir,
        Guid clientSetId,
        DateTimeOffset loggedAt) : base(id)
    {
        SessionExerciseId = sessionExerciseId;
        Order = order;
        Kind = kind;
        Weight = weight;
        Reps = reps;
        Rir = rir;
        ClientSetId = clientSetId;
        LoggedAt = loggedAt;
    }

    public SessionExerciseId SessionExerciseId { get; private set; }
    public int Order { get; private set; }
    public SetKind Kind { get; private set; }
    public Weight Weight { get; private set; }
    public int Reps { get; private set; }
    public int? Rir { get; private set; }

    /// <summary>Client-generated identity that makes logging idempotent over a flaky connection.</summary>
    public Guid ClientSetId { get; private set; }
    public DateTimeOffset LoggedAt { get; private set; }

    internal static Result<LoggedSet> Create(
        SessionExerciseId sessionExerciseId,
        int order,
        SetKind kind,
        Weight weight,
        int reps,
        int? rir,
        Guid clientSetId,
        DateTimeOffset loggedAt)
    {
        var validated = Validate(reps, rir);
        return validated.IsFailure
            ? Result<LoggedSet>.Failure(validated.Error)
            : Result<LoggedSet>.Success(new LoggedSet(
                LoggedSetId.New(), sessionExerciseId, order, kind, weight, reps, rir, clientSetId, loggedAt));
    }

    internal Result Update(Weight weight, int reps, int? rir)
    {
        var validated = Validate(reps, rir);
        if (validated.IsFailure)
            return validated;

        Weight = weight;
        Reps = reps;
        Rir = rir;
        return Result.Success();
    }

    internal void SetOrder(int order) => Order = order;

    private static Result Validate(int reps, int? rir)
    {
        if (reps is < MinReps or > MaxReps)
            return Result.Failure(new Error(
                TrainingErrors.SetRepsOutOfBounds, $"Reps must be between {MinReps} and {MaxReps}."));

        if (rir is < MinRir or > MaxRir)
            return Result.Failure(new Error(
                TrainingErrors.SetRirOutOfBounds, $"Reps in reserve must be between {MinRir} and {MaxRir}."));

        return Result.Success();
    }
}
