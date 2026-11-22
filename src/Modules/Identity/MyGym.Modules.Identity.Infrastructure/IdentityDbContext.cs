using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Identity.Domain;
using MyGym.Shared.Infrastructure.Persistence;

namespace MyGym.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, IDomainEventDispatcher? dispatcher)
    : ModuleDbContext(options, dispatcher), IIdentityUnitOfWork
{
    public const string Schema = "identity";

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new AppUserConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddTypedIds();
}
