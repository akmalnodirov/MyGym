using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

internal sealed class WorkoutSessionRepository(TrainingDbContext context) : IWorkoutSessionRepository
{
    public Task<WorkoutSession?> GetByIdAsync(SessionId id, CancellationToken ct) =>
        context.Sessions.SingleOrDefaultAsync(s => s.Id == id, ct);

    public Task<WorkoutSession?> GetInProgressForUserAsync(UserId userId, CancellationToken ct) =>
        context.Sessions.SingleOrDefaultAsync(
            s => s.UserId == userId && s.Status == SessionStatus.InProgress, ct);

    public async Task AddAsync(WorkoutSession session, CancellationToken ct) =>
        await context.Sessions.AddAsync(session, ct);

    public Task<LoggedSet?> FindLoggedSetByClientSetIdAsync(
        SessionExerciseId sessionExerciseId, Guid clientSetId, CancellationToken ct) =>
        context.Set<LoggedSet>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.SessionExerciseId == sessionExerciseId && s.ClientSetId == clientSetId, ct);
}
