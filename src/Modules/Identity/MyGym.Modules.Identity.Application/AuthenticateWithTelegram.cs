using Microsoft.Extensions.Options;
using MyGym.Modules.Identity.Domain;
using MyGym.Shared.Infrastructure.Auth;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Application;

public sealed record AuthenticateWithTelegramCommand(string InitData);

public sealed record AuthTokensResponse(
    string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public sealed class AuthenticateWithTelegramCommandHandler(
    ITelegramInitDataValidator validator,
    IAppUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IJwtTokenService tokens,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IOptions<JwtOptions> jwtOptions)
    : ICommandHandler<AuthenticateWithTelegramCommand, AuthTokensResponse>
{
    public async Task<Result<AuthTokensResponse>> HandleAsync(
        AuthenticateWithTelegramCommand command, CancellationToken ct)
    {
        var validated = validator.Validate(command.InitData);
        if (validated.IsFailure)
            return Result<AuthTokensResponse>.Failure(validated.Error);

        var info = validated.Value;
        var now = timeProvider.GetUtcNow();

        var user = await users.GetByTelegramUserIdAsync(info.TelegramUserId, ct);
        if (user is null)
        {
            var registered = AppUser.Register(
                info.TelegramUserId, info.FirstName, info.Username, info.LanguageCode, now);

            if (registered.IsFailure)
                return Result<AuthTokensResponse>.Failure(registered.Error);

            user = registered.Value;
            await users.AddAsync(user, ct);
        }
        else
        {
            user.TouchLastSeen(now);
        }

        var (refreshToken, refreshTokenHash) = tokens.CreateRefreshToken();
        await refreshTokens.AddAsync(
            RefreshToken.Issue(user.Id, refreshTokenHash, now, now.Add(jwtOptions.Value.RefreshTokenLifetime)),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        return Result<AuthTokensResponse>.Success(new AuthTokensResponse(
            tokens.CreateAccessToken(user.Id),
            refreshToken,
            now.Add(jwtOptions.Value.AccessTokenLifetime)));
    }
}
