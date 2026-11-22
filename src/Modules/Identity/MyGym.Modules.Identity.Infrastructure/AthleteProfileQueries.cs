using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Identity.Contracts;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Infrastructure;

internal sealed class AthleteProfileQueries(IdentityDbContext context) : IAthleteProfileQueries
{
    public Task<LoadingPreferences?> GetLoadingPreferencesAsync(UserId userId, CancellationToken ct) =>
        context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new LoadingPreferences(
                u.Profile.PreferredUnit, u.Profile.LoadingIncrement.Value))
            .SingleOrDefaultAsync(ct);
}
