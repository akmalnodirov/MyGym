using MyGym.SharedKernel;

namespace MyGym.Modules.ExerciseCatalog.Domain;

public interface IExerciseRepository
{
    Task<Exercise?> GetByIdAsync(ExerciseId id, CancellationToken ct);
    Task<Exercise?> GetBySlugAsync(string slug, CancellationToken ct);
    Task<bool> ExistsAsync(ExerciseId id, CancellationToken ct);
    Task AddAsync(Exercise exercise, CancellationToken ct);
}
