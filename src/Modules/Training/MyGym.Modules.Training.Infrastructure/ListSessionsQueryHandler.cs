using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Application.Sessions;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

/// <summary>
/// A read-side handler, so it lives in Infrastructure and projects straight to the response type.
/// No aggregate is materialised to render a list.
/// </summary>
/// <remarks>
/// RULING R2: <see cref="WorkoutSession.Exercises"/> and <see cref="SessionExercise.Sets"/> are
/// computed accessors that re-sort a fresh copy of the private "_exercises"/"_sets" backing fields
/// on every access. The brief's original shape —
/// <c>s.Exercises.SelectMany(e => e.Sets).Count(set => set.Kind == SetKind.Working)</c> — reaches
/// both of those public accessors from inside a projection, which Task 12 proved (see
/// TrainingQueryTranslationTests) does not fail loudly: because the projection is the query's
/// outermost operator, EF Core's "client evaluation only in the top-level projection" carve-out
/// lets it silently fetch every session's entire exercise/set graph over the wire and count in
/// memory per row — correct, but exactly the whole-aggregate load a list screen must not pay for.
///
/// <see cref="SessionExercise"/> and <see cref="LoggedSet"/> are regular (non-owned) dependent
/// entity types related via field-named navigations (see SessionExerciseConfiguration/
/// LoggedSetConfiguration), so — following the same fallback <see cref="LastWorkingWeightQuery"/>
/// uses — this reaches them directly via <c>context.Set&lt;T&gt;()</c> instead of through
/// <c>WorkoutSession.Exercises</c>/<c>SessionExercise.Sets</c>, with the working-set count expressed
/// as a correlated scalar subquery (<c>Count</c>) per session-exercise row. That turns the brief's
/// in-memory count into a genuine server-side aggregate.
/// </remarks>
internal sealed class ListSessionsQueryHandler(TrainingDbContext context)
    : IQueryHandler<ListSessionsQuery, IReadOnlyList<SessionSummaryResponse>>
{
    public async Task<Result<IReadOnlyList<SessionSummaryResponse>>> HandleAsync(
        ListSessionsQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);

        var sessionsQuery = context.Sessions.AsNoTracking().Where(s => s.UserId == query.Actor);

        if (query.From is { } from)
            sessionsQuery = sessionsQuery.Where(s => s.StartedAt >= from);
        if (query.To is { } to)
            sessionsQuery = sessionsQuery.Where(s => s.StartedAt <= to);

        // Round trip 1: the page of sessions. Touches only Sessions' own mapped columns.
        var sessionRows = await sessionsQuery
            .OrderByDescending(s => s.StartedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(s => new { s.Id, s.Status, s.StartedAt, s.CompletedAt })
            .ToListAsync(ct);

        if (sessionRows.Count == 0)
            return Result<IReadOnlyList<SessionSummaryResponse>>.Success([]);

        var sessionIds = sessionRows.Select(s => s.Id).ToList();

        // Round trip 2: working-set counts, queried directly via context.Set<T>() against the
        // mapped SessionExercise/LoggedSet entity types — never through the Ignore'd Exercises/Sets
        // accessors above them. The per-row Count(...) is a correlated scalar subquery over
        // logged_sets, so the database does the counting; nothing but counts crosses the wire.
        var workingSetCounts = await context.Set<SessionExercise>()
            .AsNoTracking()
            .Where(e => sessionIds.Contains(e.SessionId))
            .Select(e => new
            {
                e.SessionId,
                WorkingCount = context.Set<LoggedSet>()
                    .Count(set => set.SessionExerciseId == e.Id && set.Kind == SetKind.Working)
            })
            .ToListAsync(ct);

        var countsBySession = workingSetCounts
            .GroupBy(row => row.SessionId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.WorkingCount));

        var rows = sessionRows
            .Select(s => new SessionSummaryResponse(
                s.Id.Value,
                s.Status.ToString(),
                s.StartedAt,
                s.CompletedAt,
                countsBySession.GetValueOrDefault(s.Id, 0)))
            .ToList();

        return Result<IReadOnlyList<SessionSummaryResponse>>.Success(rows);
    }
}
