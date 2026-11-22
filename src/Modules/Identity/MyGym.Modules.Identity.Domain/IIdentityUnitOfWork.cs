namespace MyGym.Modules.Identity.Domain;

public interface IIdentityUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
