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

    // Different type, same component shape as Money - used to prove GetType() guards
    // Equals/== against cross-type false positives.
    private sealed class Price(decimal amount, string currency) : ValueObject
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

    [Fact]
    public void Equality_operator_is_true_for_equal_components()
    {
        var left = new Money(5m, "USD");
        var right = new Money(5m, "USD");

        (left == right).ShouldBeTrue();
        (left != right).ShouldBeFalse();
    }

    [Fact]
    public void Equality_operator_is_false_for_different_components()
    {
        var left = new Money(5m, "USD");
        var right = new Money(5m, "EUR");

        (left == right).ShouldBeFalse();
        (left != right).ShouldBeTrue();
    }

    [Fact]
    public void Equality_operator_handles_nulls_on_either_side()
    {
        ValueObject? left = null;
        ValueObject? right = null;

        (left == right).ShouldBeTrue();
        (left != right).ShouldBeFalse();

        right = new Money(5m, "USD");

        (left == right).ShouldBeFalse();
        (left != right).ShouldBeTrue();
        (right == left).ShouldBeFalse();
        (right != left).ShouldBeTrue();
    }

    [Fact]
    public void Different_value_object_types_with_equal_components_are_not_equal()
    {
        var money = new Money(5m, "USD");
        var price = new Price(5m, "USD");

        money.Equals(price).ShouldBeFalse();
        (money == price).ShouldBeFalse();
        (money != price).ShouldBeTrue();
    }
}
