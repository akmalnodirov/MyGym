using MyGym.Modules.Training.Domain.ValueObjects;
using Shouldly;

namespace MyGym.Modules.Training.Domain.Tests;

public class RestPeriodTests
{
    [Fact]
    public void Valid_rest_window_is_created()
    {
        var rest = RestPeriod.Create(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4)).Value;

        rest.Min.ShouldBe(TimeSpan.FromMinutes(3));
        rest.Max.ShouldBe(TimeSpan.FromMinutes(4));
    }

    [Fact]
    public void Inverted_window_is_rejected()
    {
        RestPeriod.Create(TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(3))
            .Error.Code.ShouldBe("rest_period.inverted");
    }

    [Fact]
    public void Non_positive_minimum_is_rejected()
    {
        RestPeriod.Create(TimeSpan.Zero, TimeSpan.FromMinutes(2))
            .Error.Code.ShouldBe("rest_period.out_of_bounds");
    }

    [Fact]
    public void Rest_longer_than_thirty_minutes_is_rejected()
    {
        RestPeriod.Create(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(31))
            .Error.Code.ShouldBe("rest_period.out_of_bounds");
    }
}
