using Microsoft.EntityFrameworkCore;
using MyGym.Modules.ExerciseCatalog.Contracts;
using MyGym.SharedKernel;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

internal sealed class ExerciseCatalogQueries(CatalogDbContext context) : IExerciseCatalogQueries
{
    public async Task<bool> AllExistAsync(IReadOnlyCollection<ExerciseId> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return true;

        var distinct = ids.Distinct().ToList();
        var found = await context.Exercises.CountAsync(e => distinct.Contains(e.Id), ct);
        return found == distinct.Count;
    }

    public async Task<IReadOnlyList<ExerciseSummary>> SearchAsync(
        string? term, string languageCode, CancellationToken ct)
    {
        var query = context.Exercises.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.Slug, pattern)
                || e.Aliases.Any(a => EF.Functions.ILike(a, pattern))
                || e.Names.Any(n => EF.Functions.ILike(n.Value, pattern)));
        }

        var exercises = await query.OrderBy(e => e.Slug).Take(100).ToListAsync(ct);

        return exercises
            .Select(e => new ExerciseSummary(
                e.Id.Value, e.Slug, e.NameFor(languageCode), e.Equipment.ToString(),
                e.PrimaryMuscles.Select(m => m.ToString()).ToArray()))
            .ToList();
    }
}
