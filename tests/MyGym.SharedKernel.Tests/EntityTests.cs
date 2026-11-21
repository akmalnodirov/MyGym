using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.SharedKernel.Tests;

public class EntityTests
{
    private sealed record TestEvent(string What) : IDomainEvent;

    private sealed class Order(Guid id, string label) : AggregateRoot<Guid>(id)
    {
        public string Label { get; private set; } = label;

        public void Rename(string label)
        {
            Label = label;
            Raise(new TestEvent(label));
        }
    }

    // Different entity type sharing TId - used to prove GetType() guards Equals/== against
    // cross-type false positives even when the underlying id value matches.
    private sealed class Invoice(Guid id) : Entity<Guid>(id);

    [Fact]
    public void Entities_are_equal_by_id_not_by_state()
    {
        var id = Guid.NewGuid();

        new Order(id, "a").ShouldBe(new Order(id, "b"));
        new Order(Guid.NewGuid(), "a").ShouldNotBe(new Order(Guid.NewGuid(), "a"));
    }

    [Fact]
    public void Raised_events_are_collected_and_clearable()
    {
        var order = new Order(Guid.NewGuid(), "a");

        order.Rename("b");
        order.DomainEvents.Count.ShouldBe(1);

        order.ClearDomainEvents();
        order.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Equality_operator_is_true_for_same_id()
    {
        var id = Guid.NewGuid();
        var left = new Order(id, "a");
        var right = new Order(id, "b");

        (left == right).ShouldBeTrue();
        (left != right).ShouldBeFalse();
    }

    [Fact]
    public void Equality_operator_is_false_for_different_ids()
    {
        var left = new Order(Guid.NewGuid(), "a");
        var right = new Order(Guid.NewGuid(), "a");

        (left == right).ShouldBeFalse();
        (left != right).ShouldBeTrue();
    }

    [Fact]
    public void Equality_operator_handles_nulls_on_either_side()
    {
        Entity<Guid>? left = null;
        Entity<Guid>? right = null;

        (left == right).ShouldBeTrue();
        (left != right).ShouldBeFalse();

        right = new Order(Guid.NewGuid(), "a");

        (left == right).ShouldBeFalse();
        (left != right).ShouldBeTrue();
        (right == left).ShouldBeFalse();
        (right != left).ShouldBeTrue();
    }

    [Fact]
    public void Different_entity_types_with_the_same_id_are_not_equal()
    {
        var id = Guid.NewGuid();
        var order = new Order(id, "a");
        var invoice = new Invoice(id);

        order.Equals(invoice).ShouldBeFalse();
        (order == invoice).ShouldBeFalse();
        (order != invoice).ShouldBeTrue();
    }
}
