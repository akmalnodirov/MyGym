using MyGym.Modules.Training.Domain.ValueObjects;
using Shouldly;

namespace MyGym.Modules.Training.Domain.Tests;

public class RepRangeTests
{
    [Fact]
    public void Valid_range_is_created()
    {
        var range = RepRange.Create(6, 8).Value;

        range.Min.ShouldBe(6);
        range.Max.ShouldBe(8);
    }

    [Fact]
    public void A_single_rep_target_is_a_valid_range()
    {
        RepRange.Create(3, 3).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Inverted_range_is_rejected()
    {
        RepRange.Create(10, 8).Error.Code.ShouldBe("rep_range.inverted");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Minimum_below_one_is_rejected(int min)
    {
        RepRange.Create(min, 8).Error.Code.ShouldBe("rep_range.out_of_bounds");
    }

    [Fact]
    public void Maximum_above_one_hundred_is_rejected()
    {
        RepRange.Create(6, 101).Error.Code.ShouldBe("rep_range.out_of_bounds");
    }
}
