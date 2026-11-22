using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyGym.Modules.Training.Application;
using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Application.Sessions;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

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

        services.AddScoped<ILastWorkingWeightQuery, LastWorkingWeightQuery>();
        services.AddScoped<IQueryHandler<ListProgramsQuery, IReadOnlyList<ProgramSummaryResponse>>,
            ListProgramsQueryHandler>();
        services.AddScoped<IQueryHandler<ListSessionsQuery, IReadOnlyList<SessionSummaryResponse>>,
            ListSessionsQueryHandler>();

        return services;
    }
}
