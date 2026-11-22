using MyGym.SharedKernel;

namespace MyGym.Modules.ExerciseCatalog.Contracts;

public sealed record ExerciseSummary(
    Guid Id, string Slug, string Name, string Equipment, string[] PrimaryMuscles);

/// <summary>The catalog's public read surface. Other modules use this and nothing else.</summary>
public interface IExerciseCatalogQueries
{
    Task<bool> AllExistAsync(IReadOnlyCollection<ExerciseId> ids, CancellationToken ct);
    Task<IReadOnlyList<ExerciseSummary>> SearchAsync(string? term, string languageCode, CancellationToken ct);
}
