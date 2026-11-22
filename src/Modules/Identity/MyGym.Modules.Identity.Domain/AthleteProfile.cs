using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Domain;

public enum ExperienceLevel
{
    Beginner = 1,
    Intermediate = 2,
    Advanced = 3
}

public sealed class AthleteProfile : ValueObject
{
    public const int MinBirthYear = 1920;
    public const int MaxBirthYear = 2020;
    public const int MinSessionMinutes = 20;
    public const int MaxSessionMinutes = 240;

    private AthleteProfile(
        int? birthYear,
        Weight? bodyweight,
        WeightUnit preferredUnit,
        WeightIncrement loadingIncrement,
        ExperienceLevel experienceLevel,
        int? availableDaysPerWeek,
        int? sessionMinutes,
        string timeZoneId)
    {
        BirthYear = birthYear;
        Bodyweight = bodyweight;
        PreferredUnit = preferredUnit;
        LoadingIncrement = loadingIncrement;
        ExperienceLevel = experienceLevel;
        AvailableDaysPerWeek = availableDaysPerWeek;
        SessionMinutes = sessionMinutes;
        TimeZoneId = timeZoneId;
    }

    /// <summary>
    /// Used only by EF Core's materialiser when loading a profile from the database. EF Core
    /// cannot bind a structural (owned) property through a constructor parameter, so
    /// <see cref="Bodyweight"/> and <see cref="LoadingIncrement"/> are left for EF to populate
    /// via backing-field access after construction. Must never be called from application code —
    /// use <see cref="Create"/> instead.
    /// </summary>
    private AthleteProfile()
    {
        LoadingIncrement = null!;
        TimeZoneId = null!;
    }

    public int? BirthYear { get; }
    public Weight? Bodyweight { get; }
    public WeightUnit PreferredUnit { get; }
    public WeightIncrement LoadingIncrement { get; }
    public ExperienceLevel ExperienceLevel { get; }
    public int? AvailableDaysPerWeek { get; }
    public int? SessionMinutes { get; }
    public string TimeZoneId { get; }

    public static AthleteProfile Default { get; } = new(
        null, null, WeightUnit.Kilograms, WeightIncrement.DefaultKg,
        ExperienceLevel.Beginner, null, null, "UTC");

    public static Result<AthleteProfile> Create(
        int? birthYear,
        Weight? bodyweight,
        WeightUnit preferredUnit,
        WeightIncrement loadingIncrement,
        ExperienceLevel experienceLevel,
        int? availableDaysPerWeek,
        int? sessionMinutes,
        string timeZoneId)
    {
        if (birthYear is < MinBirthYear or > MaxBirthYear)
            return Result<AthleteProfile>.Failure(new Error(
                IdentityErrors.BirthYearOutOfBounds,
                $"Birth year must be between {MinBirthYear} and {MaxBirthYear}."));

        if (availableDaysPerWeek is < 1 or > 7)
            return Result<AthleteProfile>.Failure(new Error(
                IdentityErrors.AvailableDaysOutOfBounds, "Available days must be between 1 and 7."));

        if (sessionMinutes is < MinSessionMinutes or > MaxSessionMinutes)
            return Result<AthleteProfile>.Failure(new Error(
                IdentityErrors.SessionMinutesOutOfBounds,
                $"Session length must be between {MinSessionMinutes} and {MaxSessionMinutes} minutes."));

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
            return Result<AthleteProfile>.Failure(new Error(
                IdentityErrors.TimeZoneUnknown, "Unknown time zone."));

        return Result<AthleteProfile>.Success(new AthleteProfile(
            birthYear, bodyweight, preferredUnit, loadingIncrement,
            experienceLevel, availableDaysPerWeek, sessionMinutes, timeZoneId));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return BirthYear;
        yield return Bodyweight;
        yield return PreferredUnit;
        yield return LoadingIncrement;
        yield return ExperienceLevel;
        yield return AvailableDaysPerWeek;
        yield return SessionMinutes;
        yield return TimeZoneId;
    }
}
