using System.Reflection;
using System.Runtime.ExceptionServices;
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

                try
                {
                    // A handler implemented as a non-async method that throws before returning its
                    // Task throws synchronously, right here, and MethodBase.Invoke always wraps that
                    // in a TargetInvocationException — losing the original exception type at the call
                    // site. A handler implemented as `async` never throws synchronously (the compiler
                    // always returns a Task, faulted if necessary), so this catch never fires for it;
                    // the `await` below unwraps that case's real exception on its own.
                    await (Task)method.Invoke(handler, [domainEvent, ct])!;
                }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                }
            }
        }
    }
}
