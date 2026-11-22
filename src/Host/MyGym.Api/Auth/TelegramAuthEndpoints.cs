using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using MyGym.Api.Errors;
using MyGym.Modules.Identity.Application;
using MyGym.SharedKernel;

namespace MyGym.Api.Auth;

public static class TelegramAuthEndpoints
{
    public sealed record TelegramSignInRequest([Required] string InitData);
    public sealed record RefreshRequest([Required] string RefreshToken);

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
        }).WithMetadata(new RequestSizeLimitAttribute(8 * 1024));

        group.MapPost("/refresh", async (
            RefreshRequest request,
            ICommandHandler<RefreshTokensCommand, AuthTokensResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new RefreshTokensCommand(request.RefreshToken), ct);
            return result.ToHttpResult();
        }).WithMetadata(new RequestSizeLimitAttribute(2 * 1024));
    }
}
