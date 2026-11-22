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

    /// <summary>
    /// Used only by EF Core's materialiser when loading an exercise from the database. EF Core
    /// cannot bind a converted collection property (<see cref="PrimaryMuscles"/>,
    /// <see cref="SecondaryMuscles"/>) or a structural collection (<see cref="Names"/>) through a
    /// constructor parameter, so they are left for EF to populate via property setters /
    /// backing-field access after construction. Must never be called from application code — use
    /// <see cref="Create"/> or <see cref="CreateCustom"/> instead.
    /// </summary>
    private Exercise() : base(default!)
    {
        Slug = null!;
        PrimaryMuscles = null!;
        SecondaryMuscles = null!;
    }

    public string Slug { get; private set; }
    public MovementPattern MovementPattern { get; private set; }
    public IReadOnlyList<MuscleGroup> PrimaryMuscles { get; private set; }
    public IReadOnlyList<MuscleGroup> SecondaryMuscles { get; private set; }
    public Equipment Equipment { get; private set; }
    public UserId? OwnerUserId { get; private set; }
    public bool IsCustom => OwnerUserId is not null;

    // Minor 1: .AsReadOnly() wraps the backing list instead of returning it directly. Worse here
    // than WarmupRamp.Steps was: Exercise is a live, loaded aggregate (not a disposable value
    // object), so returning _names/_aliases as-is let a caller cast the "IReadOnlyList" straight
    // back to List<T> and mutate the catalog's actual in-memory state.
    public IReadOnlyList<ExerciseName> Names => _names.AsReadOnly();
    public IReadOnlyList<string> Aliases => _aliases.AsReadOnly();

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
            // Minor 8: Create()/CreateInternal() reject an empty name list, but EF's materialiser
            // populates _names via backing-field access and bypasses that check, so a corrupted row
            // could still reach here with zero names. _names[0] would throw an unhelpful
            // ArgumentOutOfRangeException; this is the same "should be unreachable" state I13's
            // DomainException exists for.
            ?? (_names.Count > 0
                ? _names[0].Value
                : throw new DomainException(
                    $"Exercise {Id.Value} has no names at all; NameFor('{languageCode}') has " +
                    "nothing to fall back to. This should be unreachable since Create() requires " +
                    "at least one name."));
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
