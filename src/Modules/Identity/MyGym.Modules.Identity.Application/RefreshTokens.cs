using Microsoft.Extensions.Options;
using MyGym.Modules.Identity.Domain;
using MyGym.Shared.Infrastructure.Auth;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Application;

public sealed record RefreshTokensCommand(string RefreshToken);

public sealed class RefreshTokensCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IJwtTokenService tokens,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IOptions<JwtOptions> jwtOptions)
    : ICommandHandler<RefreshTokensCommand, AuthTokensResponse>
{
    public async Task<Result<AuthTokensResponse>> HandleAsync(RefreshTokensCommand command, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var hash = tokens.HashRefreshToken(command.RefreshToken);

        var stored = await refreshTokens.FindActiveAsync(hash, now, ct);
        if (stored is null)
            return Result<AuthTokensResponse>.Failure(
                new Error(AuthErrors.RefreshTokenInvalid, "Refresh token is not valid."));

        // Rotate: the presented token is spent, a fresh one is issued.
        stored.Revoke(now);
        var (newToken, newHash) = tokens.CreateRefreshToken();
        await refreshTokens.AddAsync(
            RefreshToken.Issue(stored.UserId, newHash, now, now.Add(jwtOptions.Value.RefreshTokenLifetime)), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return Result<AuthTokensResponse>.Success(new AuthTokensResponse(
            tokens.CreateAccessToken(stored.UserId),
            newToken,
            now.Add(jwtOptions.Value.AccessTokenLifetime)));
    }
}
