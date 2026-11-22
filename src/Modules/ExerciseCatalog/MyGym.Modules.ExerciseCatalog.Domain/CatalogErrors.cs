using MyGym.Modules.ExerciseCatalog.Contracts;

namespace MyGym.Modules.ExerciseCatalog.Domain;

public static class CatalogErrors
{
    public const string SlugInvalid = "exercise.slug_invalid";
    public const string NameRequired = "exercise.name_required";
    public const string PrimaryMuscleRequired = "exercise.primary_muscle_required";
    public const string DuplicateLanguage = "exercise.duplicate_language";
    public const string LanguageCodeInvalid = "exercise.language_code_invalid";
    public const string NameValueRequired = "exercise.name_value_required";

    // RULING R26: points at the single definition in the published contract (Contracts is
    // referenced by Domain already) instead of redeclaring the literal, so Domain and
    // Training.Application's ProgramBuilder can never spell this code two different ways.
    public const string ExerciseNotFound = ExerciseCatalogErrors.ExerciseNotFound;
}
