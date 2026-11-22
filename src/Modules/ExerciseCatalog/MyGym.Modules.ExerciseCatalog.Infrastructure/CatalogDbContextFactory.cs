using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

/// <summary>
/// Used only by `dotnet ef` at design time to build the model for migrations. The connection
/// string is never connected to — it only selects the Npgsql provider.
/// </summary>
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=mygym_design;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CatalogDbContext(options, dispatcher: null);
    }
}
