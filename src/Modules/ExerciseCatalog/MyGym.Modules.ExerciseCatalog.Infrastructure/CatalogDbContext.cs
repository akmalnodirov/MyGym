using Microsoft.EntityFrameworkCore;
using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.Shared.Infrastructure.Persistence;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, IDomainEventDispatcher? dispatcher)
    : ModuleDbContext(options, dispatcher), ICatalogUnitOfWork
{
    public const string Schema = "catalog";

    public DbSet<Exercise> Exercises => Set<Exercise>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new ExerciseConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddTypedIds();
}
