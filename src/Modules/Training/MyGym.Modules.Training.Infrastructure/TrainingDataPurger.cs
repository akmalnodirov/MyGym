using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Contracts;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

internal sealed class TrainingDataPurger(TrainingDbContext context) : ITrainingDataPurger
{
    public async Task<int> PurgeForUserAsync(UserId userId, CancellationToken ct)
    {
        // Owned child rows (session_exercises, logged_sets, program_days, exercise_prescriptions)
        // cascade from their roots, so deleting the roots is sufficient and leaves nothing behind.
        var sessions = await context.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        var programs = await context.Programs.Where(p => p.OwnerUserId == userId).ExecuteDeleteAsync(ct);

        return sessions + programs;
    }
}
