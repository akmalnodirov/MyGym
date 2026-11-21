namespace MyGym.Modules.Identity.Domain;

public static class IdentityErrors
{
    public const string TelegramIdInvalid = "identity.telegram_id_invalid";
    public const string FirstNameRequired = "identity.first_name_required";
    public const string BirthYearOutOfBounds = "profile.birth_year_out_of_bounds";
    public const string AvailableDaysOutOfBounds = "profile.available_days_out_of_bounds";
    public const string SessionMinutesOutOfBounds = "profile.session_minutes_out_of_bounds";
    public const string TimeZoneUnknown = "profile.time_zone_unknown";
    public const string UserNotFound = "identity.user_not_found";
}
