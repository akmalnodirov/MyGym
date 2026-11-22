using MyGym.Api.Errors;
using MyGym.Modules.Identity.Application;
using MyGym.SharedKernel;

namespace MyGym.Api.Auth;

public static class TelegramAuthEndpoints
{
    public sealed record TelegramSignInRequest(string InitData);
    public sealed record RefreshRequest(string RefreshToken);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").AllowAnonymous();

        group.MapPost("/telegram", async (
            TelegramSignInRequest request,
            ICommandHandler<AuthenticateWithTelegramCommand, AuthTokensResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new AuthenticateWithTelegramCommand(request.InitData), ct);
            return result.ToHttpResult();
        });

        group.MapPost("/refresh", async (
            RefreshRequest request,
            ICommandHandler<RefreshTokensCommand, AuthTokensResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new RefreshTokensCommand(request.RefreshToken), ct);
            return result.ToHttpResult();
        });
    }
}
