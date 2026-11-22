namespace MyGym.SharedKernel;

public enum WeightUnit
{
    Kilograms = 1,
    Pounds = 2
}

public sealed class WeightIncrement : ValueObject
{
    public const string NotPositiveCode = "weight_increment.not_positive";

    private WeightIncrement(decimal value, WeightUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public decimal Value { get; }
    public WeightUnit Unit { get; }

    public static WeightIncrement DefaultKg { get; } = new(2.5m, WeightUnit.Kilograms);

    public static Result<WeightIncrement> Create(decimal value, WeightUnit unit)
    {
        if (!Enum.IsDefined(unit))
            return Result<WeightIncrement>.Failure(new Error("weight_increment.unit_invalid", "Unknown weight unit."));

        return value <= 0m
            ? Result<WeightIncrement>.Failure(new Error(NotPositiveCode, "Increment must be positive."))
            : Result<WeightIncrement>.Success(new WeightIncrement(value, unit));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
        yield return Unit;
    }
}

public sealed class Weight : ValueObject, IComparable<Weight>
{
    public const decimal MaximumKilograms = 1000m;
    public const decimal PoundsPerKilogram = 2.20462262m;

    private Weight(decimal value, WeightUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public decimal Value { get; }
    public WeightUnit Unit { get; }

    public static Weight Zero { get; } = new(0m, WeightUnit.Kilograms);

    public static Result<Weight> Create(decimal value, WeightUnit unit)
    {
        // C4: an undefined unit (e.g. the request-level default when "preferredUnit" is omitted,
        // or an out-of-range value that slipped past upstream validation) used to fall through to
        // the `unit == WeightUnit.Kilograms ? value : value / PoundsPerKilogram` branch below and
        // be silently treated as Pounds — dividing every weight by ~2.2 instead of failing loudly.
        if (!Enum.IsDefined(unit))
            return Result<Weight>.Failure(new Error("weight.unit_invalid", "Unknown weight unit."));

        if (value < 0m)
            return Result<Weight>.Failure(new Error("weight.negative", "Weight cannot be negative."));

        var kilograms = unit == WeightUnit.Kilograms ? value : value / PoundsPerKilogram;
        if (kilograms > MaximumKilograms)
            return Result<Weight>.Failure(
                new Error("weight.exceeds_maximum", $"Weight cannot exceed {MaximumKilograms} kg."));

        return Result<Weight>.Success(new Weight(value, unit));
    }

    /// <summary>Trusted factory for values already known to be valid (constants, persisted data).</summary>
    public static Weight Kg(decimal value) => Create(value, WeightUnit.Kilograms).Value;

    public decimal ToKilograms() =>
        Unit == WeightUnit.Kilograms ? Value : Value / PoundsPerKilogram;

    public Weight Scale(decimal factor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(factor);

        var scaledValue = Value * factor;
        var scaledKilograms = Unit == WeightUnit.Kilograms ? scaledValue : scaledValue / PoundsPerKilogram;

        if (scaledKilograms > MaximumKilograms)
            throw new ArgumentOutOfRangeException(
                nameof(factor),
                $"Scaling by {factor} produces {scaledKilograms} kg, which exceeds {MaximumKilograms} kg.");

        return new(scaledValue, Unit);
    }

    public Weight RoundDownTo(WeightIncrement increment)
    {
        if (increment.Unit != Unit)
            throw new InvalidOperationException(
                $"Cannot round a weight in {Unit} to an increment in {increment.Unit}.");

        var steps = Math.Floor(Value / increment.Value);
        return new Weight(Math.Max(0m, steps * increment.Value), Unit);
    }

    public int CompareTo(Weight? other) =>
        other is null ? 1 : ToKilograms().CompareTo(other.ToKilograms());

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
        yield return Unit;
    }
}
