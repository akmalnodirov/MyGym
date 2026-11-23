using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using MyGym.Api.Auth;
using MyGym.Api.Errors;
using MyGym.Modules.Identity.Application;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;

namespace MyGym.Api.Endpoints;

public static class ProfileEndpoints
{
    // C4: PATCH /api/me is a full replace. TimeZoneId is [Required] because an omitted value binds
    // to null, and TimeZoneInfo.TryFindSystemTimeZoneById(null, ...) throws ArgumentNullException
    // (500) rather than returning false — the exact reproduction in the review. PreferredUnit and
    // ExperienceLevel are [EnumDataType] because an omitted enum property silently binds to the
    // default (WeightUnit)0 / (ExperienceLevel)0, neither of which is a defined member.
    public sealed record UpdateProfileRequest(
        int? BirthYear,
        decimal? Bodyweight,
        [EnumDataType(typeof(WeightUnit))] WeightUnit PreferredUnit,
        decimal LoadingIncrement,
        [EnumDataType(typeof(ExperienceLevel))] ExperienceLevel ExperienceLevel,
        int? AvailableDaysPerWeek,
        int? SessionMinutes,
        [Required] string TimeZoneId);

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
        }).Produces<ProfileResponse>();

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
        }).Produces<ProfileResponse>().WithMetadata(new RequestSizeLimitAttribute(4 * 1024));

        group.MapDelete("", async (
            ICurrentUser me, ICommandHandler<DeleteAccountCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DeleteAccountCommand(me.UserId), ct)).ToHttpResult());
    }
}
