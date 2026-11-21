using MyGym.Modules.Identity.Contracts;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Domain;

public sealed class AppUser : AggregateRoot<UserId>
{
    private AppUser(
        UserId id,
        long telegramUserId,
        string firstName,
        string? username,
        string? languageCode,
        DateTimeOffset createdAt) : base(id)
    {
        TelegramUserId = telegramUserId;
        FirstName = firstName;
        Username = username;
        LanguageCode = languageCode;
        Profile = AthleteProfile.Default;
        CreatedAt = createdAt;
        LastSeenAt = createdAt;
    }

    public long TelegramUserId { get; private set; }
    public string FirstName { get; private set; }
    public string? Username { get; private set; }
    public string? LanguageCode { get; private set; }
    public AthleteProfile Profile { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    public static Result<AppUser> Register(
        long telegramUserId,
        string firstName,
        string? username,
        string? languageCode,
        DateTimeOffset now)
    {
        if (telegramUserId <= 0)
            return Result<AppUser>.Failure(new Error(
                IdentityErrors.TelegramIdInvalid, "Telegram user id must be positive."));

        var trimmed = firstName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result<AppUser>.Failure(new Error(
                IdentityErrors.FirstNameRequired, "A first name is required."));

        var user = new AppUser(UserId.New(), telegramUserId, trimmed, username, languageCode, now);
        user.Raise(new AppUserRegistered(user.Id, telegramUserId, now));
        return Result<AppUser>.Success(user);
    }

    public Result UpdateProfile(AthleteProfile profile)
    {
        Profile = profile;
        return Result.Success();
    }

    public void TouchLastSeen(DateTimeOffset now)
    {
        if (now > LastSeenAt)
            LastSeenAt = now;
    }
}
