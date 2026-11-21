using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.SharedKernel.Tests;

public class ValueObjectTests
{
    private sealed class Money(decimal amount, string currency) : ValueObject
    {
        public decimal Amount { get; } = amount;
        public string Currency { get; } = currency;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    [Fact]
    public void Value_objects_with_equal_components_are_equal()
    {
        new Money(5m, "USD").ShouldBe(new Money(5m, "USD"));
        new Money(5m, "USD").GetHashCode().ShouldBe(new Money(5m, "USD").GetHashCode());
    }

    [Fact]
    public void Value_objects_with_different_components_are_not_equal()
    {
        new Money(5m, "USD").ShouldNotBe(new Money(5m, "EUR"));
    }
}
