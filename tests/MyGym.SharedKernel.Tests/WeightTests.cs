using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.SharedKernel.Tests;

public class WeightTests
{
    [Fact]
    public void Negative_weight_is_rejected()
    {
        var result = Weight.Create(-1m, WeightUnit.Kilograms);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("weight.negative");
    }

    [Fact]
    public void Weight_above_the_sane_maximum_is_rejected()
    {
        Weight.Create(1000.1m, WeightUnit.Kilograms).Error.Code.ShouldBe("weight.exceeds_maximum");
    }

    [Fact]
    public void Pounds_convert_to_kilograms()
    {
        Weight.Create(100m, WeightUnit.Pounds).Value
            .ToKilograms().ShouldBe(45.359m, 0.001m);
    }

    [Theory]
    [InlineData(80, 0.27, 21.6)]
    [InlineData(80, 0.53, 42.4)]
    [InlineData(80, 0.80, 64.0)]
    public void Scaling_multiplies_the_value_and_keeps_the_unit(double working, double factor, double expected)
    {
        var scaled = Weight.Kg((decimal)working).Scale((decimal)factor);

        scaled.Value.ShouldBe((decimal)expected, 0.01m);
        scaled.Unit.ShouldBe(WeightUnit.Kilograms);
    }

    [Theory]
    [InlineData(21.6, 2.5, 20.0)]
    [InlineData(42.4, 2.5, 40.0)]
    [InlineData(64.0, 2.5, 62.5)]
    [InlineData(27.5, 2.5, 27.5)]
    public void Rounding_down_lands_on_a_loadable_increment(double raw, double increment, double expected)
    {
        var rounded = Weight.Kg((decimal)raw)
            .RoundDownTo(WeightIncrement.Create((decimal)increment, WeightUnit.Kilograms).Value);

        rounded.Value.ShouldBe((decimal)expected, 0.001m);
    }

    [Fact]
    public void Rounding_never_produces_a_negative_weight()
    {
        Weight.Kg(1m).RoundDownTo(WeightIncrement.DefaultKg).Value.ShouldBe(0m);
    }

    [Fact]
    public void Rounding_rejects_a_mismatched_unit()
    {
        var poundIncrement = WeightIncrement.Create(5m, WeightUnit.Pounds).Value;

        Should.Throw<InvalidOperationException>(() => Weight.Kg(100m).RoundDownTo(poundIncrement));
    }

    [Fact]
    public void Zero_or_negative_increments_are_rejected()
    {
        WeightIncrement.Create(0m, WeightUnit.Kilograms).Error.Code.ShouldBe("weight_increment.not_positive");
    }
}
