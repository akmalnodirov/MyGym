using Microsoft.EntityFrameworkCore;
using MyGym.Modules.ExerciseCatalog.Infrastructure;
using MyGym.Modules.Identity.Infrastructure;
using MyGym.Modules.Training.Infrastructure;
using Testcontainers.PostgreSql;

namespace MyGym.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("mygym_test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using (var training = CreateTrainingContext())
            await training.Database.MigrateAsync();
        await using (var identity = CreateIdentityContext())
            await identity.Database.MigrateAsync();
        await using (var catalog = CreateCatalogContext())
            await catalog.Database.MigrateAsync();
    }

    public TrainingDbContext CreateTrainingContext()
    {
        var options = new DbContextOptionsBuilder<TrainingDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TrainingDbContext(options, dispatcher: null);
    }

    public IdentityDbContext CreateIdentityContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IdentityDbContext(options, dispatcher: null);
    }

    public CatalogDbContext CreateCatalogContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CatalogDbContext(options, dispatcher: null);
    }

    /// <summary>
    /// Minor fix-before-merge item 3: used to truncate only training.*, while all test classes in
    /// <see cref="PostgresCollection"/> share this one container. That was safe only as long as
    /// execution stayed sequential and nothing else ever truncated identity.* or catalog.* — a
    /// latent cross-suite coupling that would produce confusing first-run failures rather than a
    /// clear one. Extended to all three schemas now, before the first real run, rather than
    /// debugging it later: every call site that needs catalog data already re-seeds it in its own
    /// InitializeAsync (CatalogSeeder.SeedAsync is idempotent on slug), and no test depends on a
    /// user row surviving from a previous test (every test creates its own via a fresh random
    /// Telegram id), so truncating all three schemas here is safe.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var training = CreateTrainingContext();
        await training.Database.ExecuteSqlRawAsync(
            "TRUNCATE training.sessions, training.programs CASCADE;");

        await using var identity = CreateIdentityContext();
        await identity.Database.ExecuteSqlRawAsync(
            "TRUNCATE identity.refresh_tokens, identity.users CASCADE;");

        await using var catalog = CreateCatalogContext();
        await catalog.Database.ExecuteSqlRawAsync("TRUNCATE catalog.exercises CASCADE;");
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
