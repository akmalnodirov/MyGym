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
}
