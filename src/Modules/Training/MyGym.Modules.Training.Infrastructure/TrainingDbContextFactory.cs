using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MyGym.Modules.Training.Infrastructure;

/// <summary>
/// Used only by `dotnet ef` at design time to build the model for migrations. The connection
/// string is never connected to — it only selects the Npgsql provider.
/// </summary>
public sealed class TrainingDbContextFactory : IDesignTimeDbContextFactory<TrainingDbContext>
{
    public TrainingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TrainingDbContext>()
            .UseNpgsql("Host=localhost;Database=mygym_design;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TrainingDbContext(options, dispatcher: null);
    }
}
