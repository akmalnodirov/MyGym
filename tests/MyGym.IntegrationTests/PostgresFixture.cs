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

    public async Task ResetAsync()
    {
        await using var context = CreateTrainingContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE training.sessions, training.programs CASCADE;");
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
