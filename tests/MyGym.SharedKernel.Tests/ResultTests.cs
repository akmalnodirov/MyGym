using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.SharedKernel.Tests;

public class ResultTests
{
    [Fact]
    public void Success_result_has_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_result_carries_its_error()
    {
        var error = new Error("test.failed", "it failed");

        var result = Result.Failure(error);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(error);
    }

    [Fact]
    public void Generic_success_exposes_value()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Accessing_value_of_failure_throws()
    {
        var result = Result<int>.Failure(new Error("test.failed", "it failed"));

        Should.Throw<InvalidOperationException>(() => result.Value);
    }
}
