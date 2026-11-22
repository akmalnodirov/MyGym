using Microsoft.Extensions.DependencyInjection;
using MyGym.SharedKernel;

namespace MyGym.Shared.Infrastructure.Persistence;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct);
}

/// <summary>
/// In-process dispatch. Sub-project 1 has no subscribers; the dispatcher exists so events are
/// delivered rather than dropped, and so sub-project 2 can subscribe without touching this code.
/// A transactional outbox replaces this when the first cross-module subscriber appears.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var domainEvent in events)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            foreach (var handler in services.GetServices(handlerType))
            {
                if (handler is null)
                    continue;

                var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
                await (Task)method.Invoke(handler, [domainEvent, ct])!;
            }
        }
    }
}
