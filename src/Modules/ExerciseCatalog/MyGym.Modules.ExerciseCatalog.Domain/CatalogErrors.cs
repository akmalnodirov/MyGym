namespace MyGym.Modules.ExerciseCatalog.Domain;

public static class CatalogErrors
{
    public const string SlugInvalid = "exercise.slug_invalid";
    public const string NameRequired = "exercise.name_required";
    public const string PrimaryMuscleRequired = "exercise.primary_muscle_required";
    public const string DuplicateLanguage = "exercise.duplicate_language";
    public const string LanguageCodeInvalid = "exercise.language_code_invalid";
    public const string NameValueRequired = "exercise.name_value_required";
    public const string ExerciseNotFound = "exercise.not_found";
}
