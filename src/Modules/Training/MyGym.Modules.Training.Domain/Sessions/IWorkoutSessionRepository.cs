using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Sessions;

public interface IWorkoutSessionRepository
{
    Task<WorkoutSession?> GetByIdAsync(SessionId id, CancellationToken ct);
    Task<WorkoutSession?> GetInProgressForUserAsync(UserId userId, CancellationToken ct);
    Task AddAsync(WorkoutSession session, CancellationToken ct);

    /// <summary>
    /// I2: used only to recover from <c>DuplicateClientSetIdConflictException</c> — reads the set a
    /// concurrent request just inserted directly off the LoggedSet entity type (no tracking, no
    /// aggregate reload), so the caller can return the same idempotent success the sequential retry
    /// path already returns instead of surfacing the race as an error.
    /// </summary>
    Task<LoggedSet?> FindLoggedSetByClientSetIdAsync(
        SessionExerciseId sessionExerciseId, Guid clientSetId, CancellationToken ct);
}
