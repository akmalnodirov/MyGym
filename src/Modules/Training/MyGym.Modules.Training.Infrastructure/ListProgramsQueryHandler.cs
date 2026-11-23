using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

/// <summary>
/// Returns the athlete's own programs plus every public template, newest first. A read-side
/// projection: no aggregate is materialised, and the days are counted in SQL.
/// </summary>
/// <remarks>
/// RULING R2: <see cref="TrainingProgram.Days"/> is a computed accessor that re-sorts a copy of
/// the "_days" backing field on every access; EF Core maps only the backing field (an owned
/// collection), so <c>p.Days.Count</c> cannot be translated to SQL. Verified via
/// <c>ToQueryString()</c> (see TrainingQueryTranslationTests) that this does not even fail loudly:
/// because the Select is the query's terminal/top-level projection, EF Core's "client evaluation
/// only in the top-level projection" rule lets it silently fall back to selecting every column of
/// the program AND its entire "_days" owned collection, then computing .Count in memory per row —
/// correct, but pulling the whole owned graph over the wire just to report a count. Reaching the
/// backing field directly via <see cref="EF.Property{TProperty}"/> turns this into a genuine
/// correlated "SELECT count(*) ... WHERE p.id = p0.program_id" scalar subquery instead.
/// </remarks>
internal sealed class ListProgramsQueryHandler(TrainingDbContext context)
    : IQueryHandler<ListProgramsQuery, IReadOnlyList<ProgramSummaryResponse>>
{
    public async Task<Result<IReadOnlyList<ProgramSummaryResponse>>> HandleAsync(
        ListProgramsQuery query, CancellationToken ct)
    {
        var rows = await context.Programs
            .AsNoTracking()
            .Where(p => p.OwnerUserId == query.Actor
                        || (p.Visibility == ProgramVisibility.Public && p.Status == ProgramStatus.Active))
            .OrderByDescending(p => p.OwnerUserId == query.Actor)
            .ThenBy(p => p.Name)
            .Select(p => new ProgramSummaryResponse(
                p.Id.Value,
                p.Name,
                p.Status,
                p.Visibility,
                EF.Property<List<ProgramDay>>(p, "_days").Count,
                p.OwnerUserId == query.Actor))
            .ToListAsync(ct);

        return Result<IReadOnlyList<ProgramSummaryResponse>>.Success(rows);
    }

    // Archived programs are hidden from other athletes but remain visible to their owner, which is
    // why the Status filter above applies only to the public branch of the predicate.
}
