using MyGym.Modules.Identity.Contracts;

namespace MyGym.Modules.Identity.Domain;

public static class IdentityErrors
{
    public const string TelegramIdInvalid = "identity.telegram_id_invalid";
    public const string FirstNameRequired = "identity.first_name_required";
    public const string BirthYearOutOfBounds = "profile.birth_year_out_of_bounds";
    public const string AvailableDaysOutOfBounds = "profile.available_days_out_of_bounds";
    public const string SessionMinutesOutOfBounds = "profile.session_minutes_out_of_bounds";
    public const string TimeZoneUnknown = "profile.time_zone_unknown";
    public const string WeightUnitInvalid = "profile.weight_unit_invalid";
    public const string ExperienceLevelInvalid = "profile.experience_level_invalid";

    // RULING R26 precedent: points at the single definition in the published contract (Domain
    // already references Contracts) instead of redeclaring the literal, so Domain and
    // Training.Application's handlers (C3) can never spell this code two different ways.
    public const string UserNotFound = IdentityContractErrors.UserNotFound;
}
