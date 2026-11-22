using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Application;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

/// <summary>
/// Read-side projection: no aggregate is materialised, no domain invariant evaluated.
/// </summary>
/// <remarks>
/// RULING R2: <see cref="WorkoutSession.Exercises"/> and <see cref="SessionExercise.Sets"/> are
/// computed accessors that re-sort a copy of a private backing field on every access; EF Core maps
/// only the backing field, so <c>s.Exercises.Select(e => ...)</c> with <c>e.Sets</c> read inside
/// the projection is not translatable and throws at query time (it would also silently succeed as
/// a client-evaluated, N+1 query against the auto-included navigations under some provider
/// versions, which is worse). Unlike <c>TrainingProgram.Days</c>/<c>ProgramDay.Prescriptions</c>,
/// which are true EF "owned" collections, <see cref="SessionExercise"/> and <see cref="LoggedSet"/>
/// are regular dependent entity types related via a field-named one-to-many (see
/// SessionExerciseConfiguration/LoggedSetConfiguration) — so instead of reaching for
/// <c>EF.Property</c> across two nested levels of navigation (a shape that risks an unsupported
/// correlated-subquery translation), this follows the brief's own fallback: two round trips, going
/// straight to the underlying entity types via <c>context.Set&lt;T&gt;()</c>, which are fully
/// mapped and queryable on their own regardless of the Ignored computed properties above them.
/// </remarks>
/// <remarks>
/// I4: the heaviest working set for a given (session, exercise) pair must be chosen by comparing
/// weights across units — 100 lb (≈45 kg) beats 50 kg numerically but is lighter — and the chosen
/// row's value and unit must come from the <em>same</em> set, not from two independently ordered
/// subqueries that could disagree on a tie. The second round trip below is therefore a flat,
/// unaggregated join: every working set logged for a matching exercise in a candidate session,
/// still carrying its own value+unit pair. The candidate set is small (at most
/// <see cref="MaxRecentSessions"/> sessions' worth of working sets for the exercises on one program
/// day), so picking the per-session-exercise heaviest set, and then the most recent session per
/// exercise, is done safely in memory instead of risking an unverifiable SQL translation that
/// cannot be exercised against a real database on this machine right now.
/// </remarks>
internal sealed class LastWorkingWeightQuery(TrainingDbContext context) : ILastWorkingWeightQuery
{
    private const int MaxRecentSessions = 40;

    public async Task<IReadOnlyDictionary<ExerciseId, Weight>> GetTopWorkingWeightsAsync(
        UserId userId, IReadOnlyCollection<ExerciseId> exerciseIds, CancellationToken ct)
    {
        if (exerciseIds.Count == 0)
            return new Dictionary<ExerciseId, Weight>();

        var ids = exerciseIds.Distinct().ToList();

        // Round trip 1: candidate sessions. Touches only Sessions' own mapped columns.
        var recentSessions = await context.Sessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == SessionStatus.Completed)
            .OrderByDescending(s => s.CompletedAt)
            .Take(MaxRecentSessions)
            .Select(s => new { s.Id, s.CompletedAt })
            .ToListAsync(ct);

        if (recentSessions.Count == 0)
            return new Dictionary<ExerciseId, Weight>();

        var sessionIds = recentSessions.Select(s => s.Id).ToList();
        var completedAtBySession = recentSessions.ToDictionary(s => s.Id, s => s.CompletedAt);

        // Round trip 2: every working set logged for a matching exercise in a candidate session,
        // as a flat join with no aggregation in SQL — see the I4 remark above for why.
        var rows = await context.Set<LoggedSet>()
            .AsNoTracking()
            .Where(set => set.Kind == SetKind.Working)
            .Join(
                context.Set<SessionExercise>()
                    .AsNoTracking()
                    .Where(e => sessionIds.Contains(e.SessionId) && ids.Contains(e.Prescription.ExerciseId)),
                set => set.SessionExerciseId,
                e => e.Id,
                (set, e) => new
                {
                    e.SessionId,
                    ExerciseId = e.Prescription.ExerciseId,
                    set.Weight.Value,
                    set.Weight.Unit
                })
            .ToListAsync(ct);

        return rows
            // Heaviest working set within each (session, exercise) pair, compared by normalised
            // kilograms so value and unit are never picked from two different rows.
            .GroupBy(row => (row.SessionId, row.ExerciseId))
            .Select(group => group
                .OrderByDescending(row => Weight.Create(row.Value, row.Unit).Value.ToKilograms())
                .First())
            // Then the most recently completed session per exercise — "last" working weight.
            .GroupBy(row => row.ExerciseId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group.OrderByDescending(row => completedAtBySession[row.SessionId]).First();
                    return Weight.Create(latest.Value, latest.Unit).Value;
                });
    }
}
