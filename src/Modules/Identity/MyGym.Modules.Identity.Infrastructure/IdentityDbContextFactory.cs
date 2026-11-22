using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MyGym.Modules.Identity.Infrastructure;

/// <summary>
/// Used only by `dotnet ef` at design time to build the model for migrations. The connection
/// string is never connected to — it only selects the Npgsql provider.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=mygym_design;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IdentityDbContext(options, dispatcher: null);
    }
}
