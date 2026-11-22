using Microsoft.EntityFrameworkCore;
using MyGym.SharedKernel;

namespace MyGym.Shared.Infrastructure.Persistence;

/// <summary>
/// Base for every module's context. Saves and then dispatches the domain events raised during the
/// change, both inside one transaction, so a failing subscriber rolls the write back.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options, IDomainEventDispatcher? dispatcher)
    : DbContext(options), IUnitOfWork
{
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var roots = ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<IHasDomainEvents>()
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        var events = roots.SelectMany(r => r.DomainEvents).ToList();

        if (dispatcher is null || events.Count == 0)
        {
            foreach (var root in roots)
                root.ClearDomainEvents();

            return await base.SaveChangesAsync(ct);
        }

        await using var transaction = await Database.BeginTransactionAsync(ct);

        // acceptAllChangesOnSuccess: false — SaveChangesAsync must not mark the change tracker
        // Unchanged until the ambient transaction has actually committed. Otherwise, if
        // DispatchAsync throws below, the database rolls back but the tracker has already moved on
        // and the events are already gone: a caller reusing this context finds nothing to re-save
        // and no events to re-raise, while the write itself never happened. Accepting changes (and
        // clearing events) only after a successful commit keeps the tracker and the database from
        // silently diverging.
        var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess: false, ct);

        await dispatcher.DispatchAsync(events, ct);
        await transaction.CommitAsync(ct);

        ChangeTracker.AcceptAllChanges();
        foreach (var root in roots)
            root.ClearDomainEvents();

        return written;
    }

    /// <summary>
    /// The synchronous SaveChanges family is a separate virtual chain from SaveChangesAsync and
    /// would otherwise silently bypass domain event dispatch entirely — no error, just a write with
    /// no events delivered. Disabled outright so that mistake fails loudly instead.
    /// </summary>
    public sealed override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not support the synchronous SaveChanges path because it would " +
            $"bypass domain event dispatch. Use {nameof(SaveChangesAsync)} instead.");

    /// <summary>
    /// R18: the two-arg async overload is a separate member of the same virtual chain as the
    /// one-arg SaveChangesAsync(CancellationToken) above — overriding one does not override the
    /// other. Left unoverridden, a caller using this overload would land on DbContext's own base
    /// implementation: no transaction, no domain-event dispatch, no error, just a write with every
    /// event silently dropped. The protection before this was "no call site happens to reach it" —
    /// a property of today's call sites, not of the type. Mirrors the SaveChanges(bool) override
    /// above.
    /// </summary>
    public sealed override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not support SaveChangesAsync(bool, CancellationToken) because " +
            $"it would bypass domain event dispatch. Use {nameof(SaveChangesAsync)}(CancellationToken) instead.");
}
