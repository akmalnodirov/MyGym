using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.IntegrationTests;

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

        await using var context = fixture.CreateIdentityContext();
        context.RefreshTokens.AddRange(active, revoked);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var foundActive = await context.RefreshTokens.SingleOrDefaultAsync(
            t => t.TokenHash == active.TokenHash && t.RevokedAt == null && t.ExpiresAt > Now,
            TestContext.Current.CancellationToken);
        var foundRevoked = await context.RefreshTokens.SingleOrDefaultAsync(
            t => t.TokenHash == revoked.TokenHash && t.RevokedAt == null && t.ExpiresAt > Now,
            TestContext.Current.CancellationToken);

        foundActive.ShouldNotBeNull();
        foundRevoked.ShouldBeNull();
    }

    [Fact]
    public async Task An_expired_token_is_not_found()
    {
        var token = RefreshToken.Issue(UserId.New(), Guid.NewGuid().ToString("N"), Now.AddDays(-60), Now.AddDays(-30));

        await using var context = fixture.CreateIdentityContext();
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await context.RefreshTokens.SingleOrDefaultAsync(
            t => t.TokenHash == token.TokenHash && t.RevokedAt == null && t.ExpiresAt > Now,
            TestContext.Current.CancellationToken);

        found.ShouldBeNull();
    }
}
