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

        // Round trip 2: the exercises logged in those sessions, with the heaviest working-set
        // weight for each exposed as a correlated scalar subquery over logged_sets.
        var rows = await context.Set<SessionExercise>()
            .AsNoTracking()
            .Where(e => sessionIds.Contains(e.SessionId) && ids.Contains(e.Prescription.ExerciseId))
            .Select(e => new
            {
                e.SessionId,
                ExerciseId = e.Prescription.ExerciseId,
                TopWorkingValue = context.Set<LoggedSet>()
                    .Where(set => set.SessionExerciseId == e.Id && set.Kind == SetKind.Working)
                    .Max(set => (decimal?)set.Weight.Value),
                Unit = context.Set<LoggedSet>()
                    .Where(set => set.SessionExerciseId == e.Id && set.Kind == SetKind.Working)
                    .Select(set => set.Weight.Unit)
                    .FirstOrDefault()
            })
            .Where(row => row.TopWorkingValue != null)
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.ExerciseId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group.OrderByDescending(row => completedAtBySession[row.SessionId]).First();
                    return Weight.Create(latest.TopWorkingValue!.Value, latest.Unit).Value;
                });
    }
}
