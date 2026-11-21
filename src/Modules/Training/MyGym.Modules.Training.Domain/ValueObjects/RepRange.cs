using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.ValueObjects;

public sealed class RepRange : ValueObject
{
    public const int AbsoluteMin = 1;
    public const int AbsoluteMax = 100;

    private RepRange(int min, int max)
    {
        Min = min;
        Max = max;
    }

    public int Min { get; }
    public int Max { get; }

    public static Result<RepRange> Create(int min, int max)
    {
        if (min < AbsoluteMin || max > AbsoluteMax)
            return Result<RepRange>.Failure(new Error(
                TrainingErrors.RepRangeOutOfBounds,
                $"Reps must be between {AbsoluteMin} and {AbsoluteMax}."));

        if (min > max)
            return Result<RepRange>.Failure(new Error(
                TrainingErrors.RepRangeInverted, "Minimum reps cannot exceed maximum reps."));

        return Result<RepRange>.Success(new RepRange(min, max));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Min;
        yield return Max;
    }
}
