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
        //
        // The two ExecuteDeleteAsync calls below are wrapped in one explicit transaction on this
        // connection — three lines, same context — so a crash between them cannot leave sessions
        // deleted but programs still present (or vice versa) for this user. This does not close
        // DeleteAccountCommandHandler's own documented gap (this purge and the identity-side delete
        // are still two separate DbContexts with no distributed transaction between them); it only
        // halves the inconsistency window that exists within this one purge.
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        var sessions = await context.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        var programs = await context.Programs.Where(p => p.OwnerUserId == userId).ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);

        return sessions + programs;
    }
}
