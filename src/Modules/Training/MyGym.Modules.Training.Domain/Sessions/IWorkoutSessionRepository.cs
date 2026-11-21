using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Sessions;

public interface IWorkoutSessionRepository
{
    Task<WorkoutSession?> GetByIdAsync(SessionId id, CancellationToken ct);
    Task<WorkoutSession?> GetInProgressForUserAsync(UserId userId, CancellationToken ct);
    Task AddAsync(WorkoutSession session, CancellationToken ct);
}
