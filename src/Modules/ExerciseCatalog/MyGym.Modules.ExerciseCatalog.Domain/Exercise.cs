using System.Text.RegularExpressions;
using MyGym.SharedKernel;

namespace MyGym.Modules.ExerciseCatalog.Domain;

public sealed partial class Exercise : AggregateRoot<ExerciseId>
{
    public const string FallbackLanguage = "en";

    private readonly List<ExerciseName> _names = [];
    private readonly List<string> _aliases = [];

    private Exercise(
        ExerciseId id,
        string slug,
        MovementPattern movementPattern,
        IEnumerable<MuscleGroup> primaryMuscles,
        IEnumerable<MuscleGroup> secondaryMuscles,
        Equipment equipment,
        IEnumerable<ExerciseName> names,
        IEnumerable<string> aliases,
        UserId? ownerUserId) : base(id)
    {
        Slug = slug;
        MovementPattern = movementPattern;
        PrimaryMuscles = primaryMuscles.Distinct().ToList();
        SecondaryMuscles = secondaryMuscles.Distinct().ToList();
        Equipment = equipment;
        OwnerUserId = ownerUserId;
        _names.AddRange(names);
        _aliases.AddRange(aliases);
    }

    public string Slug { get; private set; }
    public MovementPattern MovementPattern { get; private set; }
    public IReadOnlyList<MuscleGroup> PrimaryMuscles { get; private set; }
    public IReadOnlyList<MuscleGroup> SecondaryMuscles { get; private set; }
    public Equipment Equipment { get; private set; }
    public UserId? OwnerUserId { get; private set; }
    public bool IsCustom => OwnerUserId is not null;

    public IReadOnlyList<ExerciseName> Names => _names;
    public IReadOnlyList<string> Aliases => _aliases;

    public static Result<Exercise> Create(
        string slug,
        MovementPattern movementPattern,
        IReadOnlyList<MuscleGroup> primaryMuscles,
        IReadOnlyList<MuscleGroup> secondaryMuscles,
        Equipment equipment,
        IReadOnlyList<ExerciseName> names,
        IReadOnlyList<string> aliases) =>
        CreateInternal(slug, movementPattern, primaryMuscles, secondaryMuscles, equipment,
            names, aliases, ownerUserId: null);

    public static Result<Exercise> CreateCustom(
        UserId ownerUserId,
        string slug,
        MovementPattern movementPattern,
        IReadOnlyList<MuscleGroup> primaryMuscles,
        IReadOnlyList<MuscleGroup> secondaryMuscles,
        Equipment equipment,
        IReadOnlyList<ExerciseName> names) =>
        CreateInternal(slug, movementPattern, primaryMuscles, secondaryMuscles, equipment,
            names, [], ownerUserId);

    public string NameFor(string languageCode)
    {
        var code = languageCode?.Trim().ToLowerInvariant() ?? FallbackLanguage;

        return _names.FirstOrDefault(n => n.LanguageCode == code)?.Value
            ?? _names.FirstOrDefault(n => n.LanguageCode == FallbackLanguage)?.Value
            ?? _names[0].Value;
    }

    private static Result<Exercise> CreateInternal(
        string slug,
        MovementPattern movementPattern,
        IReadOnlyList<MuscleGroup> primaryMuscles,
        IReadOnlyList<MuscleGroup> secondaryMuscles,
        Equipment equipment,
        IReadOnlyList<ExerciseName> names,
        IReadOnlyList<string> aliases,
        UserId? ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(slug) || !SlugPattern().IsMatch(slug))
            return Result<Exercise>.Failure(new Error(
                CatalogErrors.SlugInvalid, "Slug must be lowercase kebab-case, e.g. back-squat."));

        if (names.Count == 0)
            return Result<Exercise>.Failure(new Error(
                CatalogErrors.NameRequired, "At least one name is required."));

        if (names.Select(n => n.LanguageCode).Distinct().Count() != names.Count)
            return Result<Exercise>.Failure(new Error(
                CatalogErrors.DuplicateLanguage, "Each language may appear at most once."));

        if (primaryMuscles.Count == 0)
            return Result<Exercise>.Failure(new Error(
                CatalogErrors.PrimaryMuscleRequired, "At least one primary muscle is required."));

        return Result<Exercise>.Success(new Exercise(
            ExerciseId.New(), slug, movementPattern, primaryMuscles, secondaryMuscles,
            equipment, names, aliases.Select(a => a.Trim().ToLowerInvariant()), ownerUserId));
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
