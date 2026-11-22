using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyGym.Shared.Infrastructure.Persistence;
using Shouldly;

namespace MyGym.Shared.Infrastructure.Tests;

/// <summary>
/// R34 regression: <c>DbContext.SaveChangesAsync(CancellationToken)</c> is implemented by EF Core
/// as an <em>unqualified</em> call to <c>SaveChangesAsync(acceptAllChangesOnSuccess: true,
/// cancellationToken)</c> — unqualified means virtual dispatch on the runtime type. The
/// non-transactional branch of <see cref="ModuleDbContext.SaveChangesAsync(CancellationToken)"/>
/// used to call <c>base.SaveChangesAsync(ct)</c> (the one-arg overload) directly, which re-entered
/// this type's own <c>sealed override SaveChangesAsync(bool, CancellationToken)</c> and threw
/// <see cref="NotSupportedException"/> on every save that took that branch — i.e. every write
/// raising no domain event, which in production is most writes (repeat sign-in, token refresh,
/// profile updates, logging a set, editing a program) and in the test suite is literally every
/// call, since every persistence test passes <c>dispatcher: null</c>.
///
/// No runnable test previously covered any <c>ModuleDbContext</c> save path at all — every write
/// is exercised exclusively by the ~52 integration tests that cannot run on this machine (no
/// Docker). This uses SQLite's in-memory mode — a real relational provider, not EF Core's
/// InMemory provider — purely to drive <c>ModuleDbContext</c>'s dispatch logic through a genuine
/// <c>DbContext</c> pipeline without needing Postgres/Testcontainers.
/// </summary>
public class ModuleDbContextSaveChangesTests
{
    private sealed class TestDbContext(DbContextOptions options, IDomainEventDispatcher? dispatcher)
        : ModuleDbContext(options, dispatcher);

    private static TestDbContext CreateContext(IDomainEventDispatcher? dispatcher = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder()
            .UseSqlite(connection)
            .Options;

        return new TestDbContext(options, dispatcher);
    }

    [Fact]
    public async Task SaveChangesAsync_with_no_dispatcher_and_nothing_tracked_returns_zero()
    {
        // The exact branch R34 broke: dispatcher is null, so ModuleDbContext.SaveChangesAsync
        // takes the non-transactional path and must reach DbContext's real SaveChanges logic
        // through the two-arg overload, not re-enter its own throwing override. Before the fix
        // this threw NotSupportedException; after the fix it returns 0 (nothing tracked).
        await using var context = CreateContext(dispatcher: null);

        var result = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        result.ShouldBe(0);
    }

    [Fact]
    public void SaveChanges_bool_overload_still_throws()
    {
        // R18: the synchronous SaveChanges(bool) override must stay disabled — pinned by a
        // running test, not only by inspection.
        using var context = CreateContext();

        Should.Throw<NotSupportedException>(() => context.SaveChanges(acceptAllChangesOnSuccess: true));
    }

    [Fact]
    public async Task SaveChangesAsync_bool_overload_still_throws()
    {
        // R18/R34 companion: the two-arg async overload must also stay disabled. Without this
        // override, a caller using it would silently bypass domain-event dispatch entirely — no
        // transaction, no error, just a write with every event dropped.
        await using var context = CreateContext();

        await Should.ThrowAsync<NotSupportedException>(() =>
            context.SaveChangesAsync(acceptAllChangesOnSuccess: true, TestContext.Current.CancellationToken));
    }
}
