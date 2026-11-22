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
    Task RevokeAllForUserAsync(UserId userId, DateTimeOffset now, CancellationToken ct);
}
