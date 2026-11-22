using Microsoft.EntityFrameworkCore;
using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.SharedKernel;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

internal sealed class ExerciseRepository(CatalogDbContext context) : IExerciseRepository
{
    public Task<Exercise?> GetByIdAsync(ExerciseId id, CancellationToken ct) =>
        context.Exercises.SingleOrDefaultAsync(e => e.Id == id, ct);

    public Task<Exercise?> GetBySlugAsync(string slug, CancellationToken ct) =>
        context.Exercises.SingleOrDefaultAsync(e => e.Slug == slug, ct);

    public Task<bool> ExistsAsync(ExerciseId id, CancellationToken ct) =>
        context.Exercises.AnyAsync(e => e.Id == id, ct);

    public async Task AddAsync(Exercise exercise, CancellationToken ct) =>
        await context.Exercises.AddAsync(exercise, ct);
}
