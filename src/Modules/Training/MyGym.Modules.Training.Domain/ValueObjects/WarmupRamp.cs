using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.ValueObjects;

public sealed class WarmupStep : ValueObject
{
    public const int MinReps = 1;
    public const int MaxReps = 30;

    private WarmupStep(decimal percentOfWorking, int reps, bool isOptional)
    {
        PercentOfWorking = percentOfWorking;
        Reps = reps;
        IsOptional = isOptional;
    }

    /// <summary>Fraction of the working weight, exclusive of 0 and 1 — 0.53 means 53%.</summary>
    public decimal PercentOfWorking { get; }
    public int Reps { get; }
    public bool IsOptional { get; }

    public static Result<WarmupStep> Create(decimal percentOfWorking, int reps, bool isOptional)
    {
        if (percentOfWorking <= 0m || percentOfWorking >= 1m)
            return Result<WarmupStep>.Failure(new Error(
                TrainingErrors.WarmupPercentOutOfBounds,
                "Warm-up percentage must be above 0% and below 100% of the working weight."));

        if (reps is < MinReps or > MaxReps)
            return Result<WarmupStep>.Failure(new Error(
                TrainingErrors.WarmupRepsOutOfBounds,
                $"Warm-up reps must be between {MinReps} and {MaxReps}."));

        return Result<WarmupStep>.Success(new WarmupStep(percentOfWorking, reps, isOptional));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return PercentOfWorking;
        yield return Reps;
        yield return IsOptional;
    }
}

public sealed record ResolvedWarmupStep(Weight Weight, int Reps, bool IsOptional);

public sealed class WarmupRamp : ValueObject
{
    public const int MaxSteps = 5;

    private readonly List<WarmupStep> _steps;

    private WarmupRamp(List<WarmupStep> steps) => _steps = steps;

    // Minor 1: .AsReadOnly() wraps the backing list instead of returning it directly — returning
    // _steps as-is lets a caller cast the "IReadOnlyList" straight back to List<WarmupStep> and
    // mutate it, bypassing every invariant this type enforces in Create().
    public IReadOnlyList<WarmupStep> Steps => _steps.AsReadOnly();

    public static WarmupRamp Empty { get; } = new([]);

    public static Result<WarmupRamp> Create(IEnumerable<WarmupStep> steps)
    {
        var ordered = steps.ToList();

        if (ordered.Count > MaxSteps)
            return Result<WarmupRamp>.Failure(new Error(
                TrainingErrors.WarmupTooManySteps, $"A ramp may have at most {MaxSteps} steps."));

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].PercentOfWorking <= ordered[i - 1].PercentOfWorking)
                return Result<WarmupRamp>.Failure(new Error(
                    TrainingErrors.WarmupPercentNotAscending,
                    "Warm-up percentages must increase strictly from one step to the next."));
        }

        return Result<WarmupRamp>.Success(new WarmupRamp(ordered));
    }

    /// <summary>
    /// Turns percentages into weights that can physically be loaded. Rounds <em>down</em> to the
    /// nearest increment, so a resolved warm-up is never heavier than intended.
    /// </summary>
    public IReadOnlyList<ResolvedWarmupStep> Resolve(Weight working, WeightIncrement rounding) =>
        _steps
            .Select(step => new ResolvedWarmupStep(
                working.Scale(step.PercentOfWorking).RoundDownTo(rounding),
                step.Reps,
                step.IsOptional))
            .ToList();

    protected override IEnumerable<object?> GetEqualityComponents() => _steps;
}
