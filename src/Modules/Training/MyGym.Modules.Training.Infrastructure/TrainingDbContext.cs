using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.Shared.Infrastructure.Persistence;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

public sealed class TrainingDbContext(DbContextOptions<TrainingDbContext> options, IDomainEventDispatcher? dispatcher)
    : ModuleDbContext(options, dispatcher), ITrainingUnitOfWork
{
    public const string Schema = "training";

    public DbSet<TrainingProgram> Programs => Set<TrainingProgram>();
    public DbSet<WorkoutSession> Sessions => Set<WorkoutSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new TrainingProgramConfiguration());

        // LoggedSet and SessionExercise establish the relationships that create the "_sets" and
        // "_exercises" navigations; they must be applied before WorkoutSessionConfiguration calls
        // Navigation("_exercises").AutoInclude(), which requires that navigation to already exist.
        modelBuilder.ApplyConfiguration(new LoggedSetConfiguration());
        modelBuilder.ApplyConfiguration(new SessionExerciseConfiguration());
        modelBuilder.ApplyConfiguration(new WorkoutSessionConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddTypedIds();
        configurationBuilder.Properties<RepRange>().HaveConversion<RepRangeConverter>();
        configurationBuilder.Properties<RestPeriod>().HaveConversion<RestPeriodConverter>();
        configurationBuilder.Properties<WarmupRamp>()
            .HaveConversion<WarmupRampConverter, WarmupRampComparer>()
            .HaveColumnType("jsonb");

        // Weight, SessionOrigin and PrescriptionSnapshot are value objects bound through their
        // owners' constructors (WorkoutSession, LoggedSet, SessionExercise). EF Core cannot bind an
        // owned-entity navigation through a constructor parameter, and OwnedNavigationBuilder has no
        // ComplexProperty overload to configure a nested complex type from inside an owned entity
        // either — but registering the CLR type as complex here, in ConfigureConventions, applies
        // model-wide before constructor binding is evaluated, which fixing it at the OnModelCreating
        // call site (OwnsOne, or an explicit ComplexProperty) did not.
        configurationBuilder.ComplexProperties<Weight>();
        configurationBuilder.ComplexProperties<SessionOrigin>();
        configurationBuilder.ComplexProperties<PrescriptionSnapshot>();
    }
}
