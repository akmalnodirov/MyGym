using Microsoft.EntityFrameworkCore;
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
        await using var context = CreateTrainingContext();
        await context.Database.MigrateAsync();
    }

    public TrainingDbContext CreateTrainingContext()
    {
        var options = new DbContextOptionsBuilder<TrainingDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TrainingDbContext(options, dispatcher: null);
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
