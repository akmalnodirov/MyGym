using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyGym.Modules.ExerciseCatalog.Domain;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

public static class CatalogInfrastructureExtensions
{
    public static IServiceCollection AddCatalogInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CatalogDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", CatalogDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IExerciseRepository, ExerciseRepository>();
        services.AddScoped<ICatalogUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());

        return services;
    }
}
