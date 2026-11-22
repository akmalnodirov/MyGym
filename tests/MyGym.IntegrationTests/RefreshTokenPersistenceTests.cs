using MyGym.Modules.Identity.Domain;
using MyGym.Modules.Identity.Infrastructure;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.IntegrationTests;

/// <summary>
/// Unrun-tests finding #3: both tests here used to hand-roll the
/// <c>TokenHash == x &amp;&amp; RevokedAt == null &amp;&amp; ExpiresAt > now</c> predicate inline
/// instead of calling <see cref="RefreshTokenRepository.FindActiveAsync"/>, so both stayed green
/// even if that method dropped its <c>RevokedAt == null</c> clause entirely — they were testing
/// EF Core's own ability to translate a LINQ predicate to SQL, not the production query path. Both
/// now go through the real repository, reading with a fresh <see cref="IdentityDbContext"/>
/// separate from the one used to write, so identity resolution cannot hand back the in-memory
/// instance (with its in-memory property values) instead of a genuine round trip through the
/// database.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class RefreshTokenPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_active_token_is_found_and_a_revoked_one_is_not()
    {
        var userId = UserId.New();
        var active = RefreshToken.Issue(userId, Guid.NewGuid().ToString("N"), Now, Now.AddDays(30));
        var revoked = RefreshToken.Issue(userId, Guid.NewGuid().ToString("N"), Now, Now.AddDays(30));
        revoked.Revoke(Now);

        await using (var writeContext = fixture.CreateIdentityContext())
        {
            writeContext.RefreshTokens.AddRange(active, revoked);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = fixture.CreateIdentityContext();
        var repository = new RefreshTokenRepository(readContext);

        var foundActive = await repository.FindActiveAsync(
            active.TokenHash, Now, TestContext.Current.CancellationToken);
        var foundRevoked = await repository.FindActiveAsync(
            revoked.TokenHash, Now, TestContext.Current.CancellationToken);

        foundActive.ShouldNotBeNull();
        foundActive.Id.ShouldBe(active.Id);
        foundRevoked.ShouldBeNull();
    }

    [Fact]
    public async Task A_token_expiring_at_exactly_now_is_not_found()
    {
        // Unrun-tests finding #3's "tautology" complaint: the old version wrote a token expired
        // 30 days ago and asked whether ExpiresAt > Now held — true or false independent of
        // RevokedAt, and far enough from the boundary that a subtly wrong comparison (e.g. >= vs
        // >, or a clock off-by-one) would still pass. This pins the actual boundary: a token whose
        // ExpiresAt equals the instant being checked is not active (ExpiresAt > now must be
        // strict), which an off-by-one in FindActiveAsync's comparison would actually catch.
        var token = RefreshToken.Issue(UserId.New(), Guid.NewGuid().ToString("N"), Now.AddDays(-30), Now);

        await using (var writeContext = fixture.CreateIdentityContext())
        {
            writeContext.RefreshTokens.Add(token);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = fixture.CreateIdentityContext();
        var repository = new RefreshTokenRepository(readContext);

        var found = await repository.FindActiveAsync(
            token.TokenHash, Now, TestContext.Current.CancellationToken);

        found.ShouldBeNull();
    }
}
