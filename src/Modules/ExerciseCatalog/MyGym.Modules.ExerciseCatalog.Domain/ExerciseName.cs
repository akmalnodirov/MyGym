using MyGym.SharedKernel;

namespace MyGym.Modules.ExerciseCatalog.Domain;

public sealed class ExerciseName : ValueObject
{
    private ExerciseName(string languageCode, string value)
    {
        LanguageCode = languageCode;
        Value = value;
    }

    /// <summary>ISO 639-1, lowercase — "en", "uz", "ru".</summary>
    public string LanguageCode { get; }
    public string Value { get; }

    public static Result<ExerciseName> Create(string languageCode, string value)
    {
        var code = languageCode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (code.Length != 2 || !code.All(char.IsAsciiLetterLower))
            return Result<ExerciseName>.Failure(new Error(
                CatalogErrors.LanguageCodeInvalid, "Language code must be two ASCII letters."));

        var name = value?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 120)
            return Result<ExerciseName>.Failure(new Error(
                CatalogErrors.NameValueRequired, "A name of 1 to 120 characters is required."));

        return Result<ExerciseName>.Success(new ExerciseName(code, name));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return LanguageCode;
        yield return Value;
    }
}
