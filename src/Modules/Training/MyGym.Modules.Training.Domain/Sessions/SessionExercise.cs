using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Sessions;

public sealed class SessionExercise : Entity<SessionExerciseId>
{
    private readonly List<LoggedSet> _sets = [];

    private SessionExercise(
        SessionExerciseId id,
        SessionId sessionId,
        int order,
        PrescriptionSnapshot prescription) : base(id)
    {
        SessionId = sessionId;
        Order = order;
        Prescription = prescription;
    }

    /// <summary>
    /// Used only by EF Core's materialiser when loading a session from the database. EF Core
    /// cannot bind a structural (owned or complex) property through a constructor parameter, so
    /// <see cref="Prescription"/> is left for EF's property setter to populate after
    /// construction. Must never be called from application code — use <see cref="Create"/> instead.
    /// </summary>
    private SessionExercise() : base(default!)
    {
        Prescription = null!;
    }

    public SessionId SessionId { get; private set; }
    public int Order { get; private set; }
    public PrescriptionSnapshot Prescription { get; private set; }
    public ExerciseId ExerciseId => Prescription.ExerciseId;

    public IReadOnlyList<LoggedSet> Sets => _sets.OrderBy(s => s.Order).ToList();

    internal static SessionExercise Create(SessionId sessionId, int order, PrescriptionSnapshot prescription) =>
        new(SessionExerciseId.New(), sessionId, order, prescription);

    internal void SetOrder(int order) => Order = order;

    internal LoggedSet? FindByClientSetId(Guid clientSetId) =>
        _sets.SingleOrDefault(s => s.ClientSetId == clientSetId);

    internal Result<LoggedSet> AddSet(
        SetKind kind, Weight weight, int reps, int? rir, Guid clientSetId, DateTimeOffset loggedAt)
    {
        var result = LoggedSet.Create(Id, _sets.Count + 1, kind, weight, reps, rir, clientSetId, loggedAt);
        if (result.IsFailure)
            return result;

        _sets.Add(result.Value);
        return result;
    }

    internal Result UpdateSet(LoggedSetId setId, Weight weight, int reps, int? rir)
    {
        var set = _sets.SingleOrDefault(s => s.Id == setId);
        return set is null
            ? Result.Failure(new Error(TrainingErrors.SetNotFound, "Set not found."))
            : set.Update(weight, reps, rir);
    }

    internal Result RemoveSet(LoggedSetId setId)
    {
        var set = _sets.SingleOrDefault(s => s.Id == setId);
        if (set is null)
            return Result.Failure(new Error(TrainingErrors.SetNotFound, "Set not found."));

        _sets.Remove(set);

        var order = 1;
        foreach (var remaining in _sets.OrderBy(s => s.Order).ToList())
            remaining.SetOrder(order++);

        return Result.Success();
    }

    internal int CountWorkingSets() => _sets.Count(s => s.Kind == SetKind.Working);
}
