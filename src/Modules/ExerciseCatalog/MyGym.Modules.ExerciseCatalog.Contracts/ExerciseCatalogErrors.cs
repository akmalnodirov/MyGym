namespace MyGym.Modules.ExerciseCatalog.Contracts;

/// <summary>
/// Error codes that cross the catalog's module boundary — part of the published contract, not an
/// implementation detail. ExerciseCatalog.Domain's <c>CatalogErrors.ExerciseNotFound</c> points
/// back at <see cref="ExerciseNotFound"/> rather than redeclaring the string, and
/// Training.Application's <c>ProgramBuilder</c> reads it directly (Training.Application may
/// reference ExerciseCatalog.Contracts but not ExerciseCatalog.Domain), so there is exactly one
/// definition of this wire-contract code no matter which side of the module boundary needs it
/// (RULING R26 — Task 12 review).
/// </summary>
public static class ExerciseCatalogErrors
{
    public const string ExerciseNotFound = "exercise.not_found";
}
