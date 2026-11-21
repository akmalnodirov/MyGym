using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.Modules.Training.Domain.Tests;

public class WarmupRampTests
{
    private static WarmupStep Step(decimal percent, int reps, bool optional = false) =>
        WarmupStep.Create(percent, reps, optional).Value;

    [Fact]
    public void An_empty_ramp_is_valid_and_resolves_to_nothing()
    {
        WarmupRamp.Empty.Steps.ShouldBeEmpty();
        WarmupRamp.Empty.Resolve(Weight.Kg(100m), WeightIncrement.DefaultKg).ShouldBeEmpty();
    }

    [Fact]
    public void Percentages_must_be_strictly_ascending()
    {
        var result = WarmupRamp.Create([Step(0.60m, 8), Step(0.50m, 5)]);

        result.Error.Code.ShouldBe("warmup.percent_not_ascending");
    }

    [Fact]
    public void Equal_consecutive_percentages_are_rejected()
    {
        WarmupRamp.Create([Step(0.50m, 8), Step(0.50m, 5)])
            .Error.Code.ShouldBe("warmup.percent_not_ascending");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.2)]
    public void A_warmup_step_at_or_above_the_working_weight_is_rejected(double percent)
    {
        WarmupStep.Create((decimal)percent, 5, false).Error.Code.ShouldBe("warmup.percent_out_of_bounds");
    }

    [Fact]
    public void A_non_positive_percentage_is_rejected()
    {
        WarmupStep.Create(0m, 5, false).Error.Code.ShouldBe("warmup.percent_out_of_bounds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Warmup_reps_outside_one_to_thirty_are_rejected(int reps)
    {
        WarmupStep.Create(0.5m, reps, false).Error.Code.ShouldBe("warmup.reps_out_of_bounds");
    }

    [Fact]
    public void More_than_five_steps_are_rejected()
    {
        var steps = new[] { Step(0.2m, 10), Step(0.3m, 8), Step(0.4m, 6), Step(0.5m, 5), Step(0.6m, 3), Step(0.7m, 2) };

        WarmupRamp.Create(steps).Error.Code.ShouldBe("warmup.too_many_steps");
    }

    [Fact]
    public void Step_count_is_checked_before_ordering()
    {
        // Six steps that are also non-ascending: if ordering were checked first this would
        // report warmup.percent_not_ascending instead.
        var steps = new[] { Step(0.6m, 10), Step(0.2m, 8), Step(0.4m, 6), Step(0.5m, 5), Step(0.3m, 3), Step(0.7m, 2) };

        WarmupRamp.Create(steps).Error.Code.ShouldBe("warmup.too_many_steps");
    }

    [Fact]
    public void Bench_ramp_resolves_to_loadable_weights()
    {
        // The real ramp from the author's Friday bench press: 27% x 10, 53% x 6, 80% x 3.
        var ramp = WarmupRamp.Create([Step(0.27m, 10), Step(0.53m, 6), Step(0.80m, 3)]).Value;

        var resolved = ramp.Resolve(Weight.Kg(80m), WeightIncrement.DefaultKg);

        resolved.Count.ShouldBe(3);
        resolved[0].Weight.Value.ShouldBe(20.0m);   // 21.6 rounds down to 20.0
        resolved[0].Reps.ShouldBe(10);
        resolved[1].Weight.Value.ShouldBe(40.0m);   // 42.4 rounds down to 40.0, never up
        resolved[2].Weight.Value.ShouldBe(62.5m);   // 64.0 rounds down to 62.5
    }

    [Fact]
    public void Resolution_preserves_step_order_reps_and_optionality()
    {
        var ramp = WarmupRamp.Create([Step(0.40m, 8), Step(0.70m, 3, optional: true)]).Value;

        var resolved = ramp.Resolve(Weight.Kg(100m), WeightIncrement.DefaultKg);

        resolved[0].Reps.ShouldBe(8);
        resolved[0].IsOptional.ShouldBeFalse();
        resolved[1].Reps.ShouldBe(3);
        resolved[1].IsOptional.ShouldBeTrue();
    }

    [Fact]
    public void Resolution_never_returns_a_weight_at_or_above_the_working_weight()
    {
        var ramp = WarmupRamp.Create([Step(0.99m, 1)]).Value;

        var resolved = ramp.Resolve(Weight.Kg(20m), WeightIncrement.DefaultKg);

        resolved[0].Weight.ToKilograms().ShouldBeLessThan(20m);
    }

    [Fact]
    public void A_light_working_weight_can_resolve_to_an_empty_bar()
    {
        var ramp = WarmupRamp.Create([Step(0.30m, 10)]).Value;

        var resolved = ramp.Resolve(Weight.Kg(5m), WeightIncrement.DefaultKg);

        resolved[0].Weight.Value.ShouldBe(0m);
    }
}
