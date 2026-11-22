using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Infrastructure;

internal sealed class RefreshTokenRepository(IdentityDbContext context) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindActiveAsync(string tokenHash, DateTimeOffset now, CancellationToken ct) =>
        context.RefreshTokens.SingleOrDefaultAsync(
            t => t.TokenHash == tokenHash && t.RevokedAt == null && t.ExpiresAt > now, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct) =>
        await context.RefreshTokens.AddAsync(token, ct);

    public async Task RevokeAllForUserAsync(UserId userId, DateTimeOffset now, CancellationToken ct)
    {
        var tokens = await context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
            token.Revoke(now);
    }
}
