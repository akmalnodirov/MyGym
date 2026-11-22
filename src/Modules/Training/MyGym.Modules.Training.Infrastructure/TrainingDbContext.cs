using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.Shared.Infrastructure.Persistence;
using MyGym.SharedKernel;
using Npgsql;

namespace MyGym.Modules.Training.Infrastructure;

public sealed class TrainingDbContext(DbContextOptions<TrainingDbContext> options, IDomainEventDispatcher? dispatcher)
    : ModuleDbContext(options, dispatcher), ITrainingUnitOfWork
{
    public const string Schema = "training";

    public DbSet<TrainingProgram> Programs => Set<TrainingProgram>();
    public DbSet<WorkoutSession> Sessions => Set<WorkoutSession>();

    /// <summary>
    /// I2: translates the two concurrency failure modes a write here can hit into the Domain
    /// exceptions in ConcurrencyExceptions.cs, so Application handlers can catch a specific type
    /// and map it to the Result the corresponding guard describes, instead of either an unmapped
    /// 500 (nothing previously caught DbUpdateException/DbUpdateConcurrencyException anywhere) or
    /// requiring Application to reference EF Core/Npgsql directly. The constraint-name constants
    /// referenced below are public specifically so this consumer could exist.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await base.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ProgramEditConflictException();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pg)
        {
            throw pg.ConstraintName switch
            {
                WorkoutSessionConfiguration.InProgressIndexName => new SessionAlreadyInProgressConflictException(),
                LoggedSetConfiguration.ClientSetIdIndexName => new DuplicateClientSetIdConflictException(),
                _ => ex
            };
        }
    }

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
