namespace MyGym.Modules.ExerciseCatalog.Domain;

public interface ICatalogUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
