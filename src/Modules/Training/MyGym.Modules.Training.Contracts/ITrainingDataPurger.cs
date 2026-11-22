using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Contracts;

/// <summary>
/// Erases every trace of an athlete's training data. Exposed through Contracts so Identity can call
/// it during account deletion without referencing Training's internals.
/// </summary>
public interface ITrainingDataPurger
{
    Task<int> PurgeForUserAsync(UserId userId, CancellationToken ct);
}
