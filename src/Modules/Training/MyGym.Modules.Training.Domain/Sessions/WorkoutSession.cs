using MyGym.Modules.Training.Contracts;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Sessions;

public enum SessionStatus
{
    InProgress = 1,
    Completed = 2,
    Abandoned = 3
}

public sealed class WorkoutSession : AggregateRoot<SessionId>
{
    private readonly List<SessionExercise> _exercises = [];

    private WorkoutSession(SessionId id, UserId userId, SessionOrigin origin, DateTimeOffset startedAt)
        : base(id)
    {
        UserId = userId;
        Origin = origin;
        StartedAt = startedAt;
        Status = SessionStatus.InProgress;
    }

    public UserId UserId { get; private set; }
    public SessionOrigin Origin { get; private set; }
    public SessionStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Weight? BodyweightAtSession { get; private set; }

    public IReadOnlyList<SessionExercise> Exercises => _exercises.OrderBy(e => e.Order).ToList();

    public int WorkingSetCount => _exercises.Sum(e => e.CountWorkingSets());

    public static Result<WorkoutSession> Start(
        UserId userId,
        SessionOrigin origin,
        IReadOnlyList<PrescriptionSnapshot> prescriptions,
        DateTimeOffset startedAt)
    {
        if (!origin.IsAdHoc && prescriptions.Count == 0)
            return Result<WorkoutSession>.Failure(new Error(
                TrainingErrors.SessionNeedsExercise,
                "A session started from a program day needs at least one exercise."));

        var session = new WorkoutSession(SessionId.New(), userId, origin, startedAt);

        var order = 1;
        foreach (var prescription in prescriptions)
            session._exercises.Add(SessionExercise.Create(session.Id, order++, prescription));

        session.Raise(new WorkoutSessionStarted(session.Id, userId, startedAt));
        return Result<WorkoutSession>.Success(session);
    }

    public Result<SessionExercise> AddExercise(PrescriptionSnapshot prescription)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return Result<SessionExercise>.Failure(open.Error);

        var exercise = SessionExercise.Create(Id, _exercises.Count + 1, prescription);
        _exercises.Add(exercise);
        return Result<SessionExercise>.Success(exercise);
    }

    /// <summary>
    /// Idempotent on <paramref name="clientSetId"/>: a retry after a dropped response returns the
    /// set that was already stored instead of creating a duplicate.
    /// </summary>
    public Result<LoggedSet> LogSet(
        SessionExerciseId sessionExerciseId,
        Guid clientSetId,
        SetKind kind,
        Weight weight,
        int reps,
        int? rir,
        DateTimeOffset loggedAt)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return Result<LoggedSet>.Failure(open.Error);

        var exercise = FindExercise(sessionExerciseId);
        if (exercise is null)
            return Result<LoggedSet>.Failure(new Error(
                TrainingErrors.SessionExerciseNotFound, "Session exercise not found."));

        var existing = exercise.FindByClientSetId(clientSetId);
        if (existing is not null)
            return Result<LoggedSet>.Success(existing);

        return exercise.AddSet(kind, weight, reps, rir, clientSetId, loggedAt);
    }

    public Result EditSet(SessionExerciseId sessionExerciseId, LoggedSetId setId, Weight weight, int reps, int? rir)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return open;

        var exercise = FindExercise(sessionExerciseId);
        return exercise is null
            ? Result.Failure(new Error(TrainingErrors.SessionExerciseNotFound, "Session exercise not found."))
            : exercise.UpdateSet(setId, weight, reps, rir);
    }

    public Result DeleteSet(SessionExerciseId sessionExerciseId, LoggedSetId setId)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return open;

        var exercise = FindExercise(sessionExerciseId);
        return exercise is null
            ? Result.Failure(new Error(TrainingErrors.SessionExerciseNotFound, "Session exercise not found."))
            : exercise.RemoveSet(setId);
    }

    public Result RecordBodyweight(Weight bodyweight)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return open;

        BodyweightAtSession = bodyweight;
        return Result.Success();
    }

    public Result Complete(DateTimeOffset completedAt)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return open;

        var workingSets = WorkingSetCount;
        if (workingSets == 0)
            return Result.Failure(new Error(
                TrainingErrors.SessionNeedsWorkingSet, "Log at least one working set before finishing."));

        Status = SessionStatus.Completed;
        CompletedAt = completedAt;
        Raise(new WorkoutSessionCompleted(Id, UserId, completedAt, workingSets));
        return Result.Success();
    }

    public Result Abandon(DateTimeOffset abandonedAt)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return open;

        Status = SessionStatus.Abandoned;
        CompletedAt = abandonedAt;
        return Result.Success();
    }

    private SessionExercise? FindExercise(SessionExerciseId id) =>
        _exercises.SingleOrDefault(e => e.Id == id);

    private Result EnsureOpen() =>
        Status == SessionStatus.InProgress
            ? Result.Success()
            : Result.Failure(new Error(TrainingErrors.SessionClosed, "This session is already closed."));
}
