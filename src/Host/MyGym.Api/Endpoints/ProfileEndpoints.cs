using MyGym.Api.Auth;
using MyGym.Api.Errors;
using MyGym.Modules.Identity.Application;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;

namespace MyGym.Api.Endpoints;

public static class ProfileEndpoints
{
    public sealed record UpdateProfileRequest(
        int? BirthYear,
        decimal? Bodyweight,
        WeightUnit PreferredUnit,
        decimal LoadingIncrement,
        ExperienceLevel ExperienceLevel,
        int? AvailableDaysPerWeek,
        int? SessionMinutes,
        string TimeZoneId);

    public static void MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me").RequireAuthorization();

        group.MapGet("", async (
            ICurrentUser currentUser,
            IQueryHandler<GetProfileQuery, ProfileResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProfileQuery(currentUser.UserId), ct);
            return result.ToHttpResult();
        });

        group.MapPatch("", async (
            UpdateProfileRequest request,
            ICurrentUser currentUser,
            ICommandHandler<UpdateProfileCommand, ProfileResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UpdateProfileCommand(
                currentUser.UserId, request.BirthYear, request.Bodyweight, request.PreferredUnit,
                request.LoadingIncrement, request.ExperienceLevel, request.AvailableDaysPerWeek,
                request.SessionMinutes, request.TimeZoneId), ct);

            return result.ToHttpResult();
        });

        group.MapDelete("", async (
            ICurrentUser me, ICommandHandler<DeleteAccountCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DeleteAccountCommand(me.UserId), ct)).ToHttpResult());
    }
}
