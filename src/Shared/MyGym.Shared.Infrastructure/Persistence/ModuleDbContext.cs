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

        var written = await base.SaveChangesAsync(ct);
        foreach (var root in roots)
            root.ClearDomainEvents();

        await dispatcher.DispatchAsync(events, ct);
        await transaction.CommitAsync(ct);

        return written;
    }
}
