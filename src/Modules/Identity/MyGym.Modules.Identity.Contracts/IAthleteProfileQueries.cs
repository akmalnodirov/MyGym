using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Contracts;

public sealed record LoadingPreferences(WeightUnit PreferredUnit, decimal Increment);

public interface IAthleteProfileQueries
{
    Task<LoadingPreferences?> GetLoadingPreferencesAsync(UserId userId, CancellationToken ct);
}
