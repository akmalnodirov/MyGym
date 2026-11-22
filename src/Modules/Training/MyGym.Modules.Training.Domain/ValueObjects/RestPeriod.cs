using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.ValueObjects;

public sealed class RestPeriod : ValueObject
{
    public static readonly TimeSpan AbsoluteMax = TimeSpan.FromMinutes(30);

    private RestPeriod(TimeSpan min, TimeSpan max)
    {
        Min = min;
        Max = max;
    }

    public TimeSpan Min { get; }
    public TimeSpan Max { get; }

    public static Result<RestPeriod> Create(TimeSpan min, TimeSpan max)
    {
        // Minor 2/5: RestPeriodConverter stores whole seconds ("{min}-{max}" as integer seconds),
        // so a sub-second component silently truncated on every round-trip through the database —
        // domain and storage disagreed about what a RestPeriod even was. Rejecting it here, before
        // any row exists, is cheaper than discovering the lossy round-trip in production.
        if (min.Ticks % TimeSpan.TicksPerSecond != 0 || max.Ticks % TimeSpan.TicksPerSecond != 0)
            return Result<RestPeriod>.Failure(new Error(
                TrainingErrors.RestPeriodOutOfBounds, "Rest must be a whole number of seconds."));

        if (min <= TimeSpan.Zero || max > AbsoluteMax)
            return Result<RestPeriod>.Failure(new Error(
                TrainingErrors.RestPeriodOutOfBounds,
                $"Rest must be positive and at most {AbsoluteMax.TotalMinutes} minutes."));

        if (min > max)
            return Result<RestPeriod>.Failure(new Error(
                TrainingErrors.RestPeriodInverted, "Minimum rest cannot exceed maximum rest."));

        return Result<RestPeriod>.Success(new RestPeriod(min, max));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Min;
        yield return Max;
    }
}
