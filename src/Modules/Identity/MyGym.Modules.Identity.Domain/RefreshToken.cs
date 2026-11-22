using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Domain;

public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken(
        Guid id, UserId userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public UserId UserId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static RefreshToken Issue(
        UserId userId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt) =>
        new(Guid.CreateVersion7(), userId, tokenHash, now, expiresAt);

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindActiveAsync(string tokenHash, DateTimeOffset now, CancellationToken ct);
    Task AddAsync(RefreshToken token, CancellationToken ct);

    /// <summary>
    /// I10: a real hard delete, not a revoke. Spec §10 requires "a real hard delete across all
    /// module schemas" on account deletion; revoking (setting RevokedAt) left the rows in place
    /// forever, still carrying the deleted user's id and token hash, with no FK to clean them up.
    /// </summary>
    Task<int> DeleteAllForUserAsync(UserId userId, CancellationToken ct);
}
