using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Application;

public sealed record ProfileResponse(
    Guid Id,
    string FirstName,
    string? Username,
    string? LanguageCode,
    int? BirthYear,
    decimal? Bodyweight,
    WeightUnit PreferredUnit,
    decimal LoadingIncrement,
    ExperienceLevel ExperienceLevel,
    int? AvailableDaysPerWeek,
    int? SessionMinutes,
    string TimeZoneId)
{
    public static ProfileResponse From(AppUser user) => new(
        user.Id.Value, user.FirstName, user.Username, user.LanguageCode,
        user.Profile.BirthYear, user.Profile.Bodyweight?.Value, user.Profile.PreferredUnit,
        user.Profile.LoadingIncrement.Value, user.Profile.ExperienceLevel,
        user.Profile.AvailableDaysPerWeek, user.Profile.SessionMinutes, user.Profile.TimeZoneId);
}

public sealed record GetProfileQuery(UserId UserId);

public sealed class GetProfileQueryHandler(IAppUserRepository users)
    : IQueryHandler<GetProfileQuery, ProfileResponse>
{
    public async Task<Result<ProfileResponse>> HandleAsync(GetProfileQuery query, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(query.UserId, ct);
        return user is null
            ? Result<ProfileResponse>.Failure(new Error(IdentityErrors.UserNotFound, "User not found."))
            : Result<ProfileResponse>.Success(ProfileResponse.From(user));
    }
}

public sealed record UpdateProfileCommand(
    UserId UserId,
    int? BirthYear,
    decimal? Bodyweight,
    WeightUnit PreferredUnit,
    decimal LoadingIncrement,
    ExperienceLevel ExperienceLevel,
    int? AvailableDaysPerWeek,
    int? SessionMinutes,
    string TimeZoneId);

public sealed class UpdateProfileCommandHandler(IAppUserRepository users, IIdentityUnitOfWork unitOfWork)
    : ICommandHandler<UpdateProfileCommand, ProfileResponse>
{
    public async Task<Result<ProfileResponse>> HandleAsync(UpdateProfileCommand command, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null)
            return Result<ProfileResponse>.Failure(new Error(IdentityErrors.UserNotFound, "User not found."));

        Weight? bodyweight = null;
        if (command.Bodyweight is { } value)
        {
            var created = Weight.Create(value, command.PreferredUnit);
            if (created.IsFailure)
                return Result<ProfileResponse>.Failure(created.Error);

            bodyweight = created.Value;
        }

        var increment = WeightIncrement.Create(command.LoadingIncrement, command.PreferredUnit);
        if (increment.IsFailure)
            return Result<ProfileResponse>.Failure(increment.Error);

        var profile = AthleteProfile.Create(
            command.BirthYear, bodyweight, command.PreferredUnit, increment.Value,
            command.ExperienceLevel, command.AvailableDaysPerWeek, command.SessionMinutes,
            command.TimeZoneId);

        if (profile.IsFailure)
            return Result<ProfileResponse>.Failure(profile.Error);

        user.UpdateProfile(profile.Value);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<ProfileResponse>.Success(ProfileResponse.From(user));
    }
}
