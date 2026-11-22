using Microsoft.EntityFrameworkCore;
using MyGym.Modules.ExerciseCatalog.Domain;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

public static class CatalogSeeder
{
    /// <summary>Idempotent: matches on slug, so re-running on every startup is safe.</summary>
    public static async Task SeedAsync(CatalogDbContext context, CancellationToken ct)
    {
        var existing = await context.Exercises.Select(e => e.Slug).ToListAsync(ct);
        var known = existing.ToHashSet();

        var missing = CatalogSeed.Exercises.Where(e => !known.Contains(e.Slug)).ToList();
        if (missing.Count == 0)
            return;

        await context.Exercises.AddRangeAsync(missing, ct);
        await context.SaveChangesAsync(ct);
    }
}
