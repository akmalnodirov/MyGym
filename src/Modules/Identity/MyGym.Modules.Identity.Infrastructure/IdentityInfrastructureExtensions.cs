using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyGym.Modules.Identity.Contracts;
using MyGym.Modules.Identity.Domain;

namespace MyGym.Modules.Identity.Infrastructure;

public static class IdentityInfrastructureExtensions
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<IdentityDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAppUserRepository, AppUserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAthleteProfileQueries, AthleteProfileQueries>();
        services.AddScoped<IIdentityUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());

        return services;
    }
}
