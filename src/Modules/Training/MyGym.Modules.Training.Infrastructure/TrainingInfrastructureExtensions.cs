using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.Sessions;

namespace MyGym.Modules.Training.Infrastructure;

public static class TrainingInfrastructureExtensions
{
    public static IServiceCollection AddTrainingInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TrainingDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TrainingDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<ITrainingProgramRepository, TrainingProgramRepository>();
        services.AddScoped<IWorkoutSessionRepository, WorkoutSessionRepository>();
        services.AddScoped<ITrainingUnitOfWork>(sp => sp.GetRequiredService<TrainingDbContext>());

        return services;
    }
}
