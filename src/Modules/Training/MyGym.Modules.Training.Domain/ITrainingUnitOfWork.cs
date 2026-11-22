namespace MyGym.Modules.Training.Domain;

/// <summary>
/// Declared in Domain so application handlers depend on an abstraction, not on EF Core.
/// Implemented by TrainingDbContext.
/// </summary>
public interface ITrainingUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
