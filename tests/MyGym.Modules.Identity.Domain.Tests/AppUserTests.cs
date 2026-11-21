using MyGym.Modules.Identity.Contracts;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.Modules.Identity.Domain.Tests;

public class AppUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Registering_links_the_telegram_account_and_applies_default_profile()
    {
        var user = AppUser.Register(123456789L, "Akmal", "akmal", "uz", Now).Value;

        user.TelegramUserId.ShouldBe(123456789L);
        user.FirstName.ShouldBe("Akmal");
        user.Username.ShouldBe("akmal");
        user.LanguageCode.ShouldBe("uz");
        user.CreatedAt.ShouldBe(Now);
        user.LastSeenAt.ShouldBe(Now);
        user.Profile.PreferredUnit.ShouldBe(WeightUnit.Kilograms);
        user.Profile.ExperienceLevel.ShouldBe(ExperienceLevel.Beginner);
        user.Profile.TimeZoneId.ShouldBe("UTC");
    }

    [Fact]
    public void Registering_raises_the_registration_event()
    {
        var user = AppUser.Register(42L, "A", null, null, Now).Value;

        var raised = user.DomainEvents.OfType<AppUserRegistered>().ShouldHaveSingleItem();
        raised.UserId.ShouldBe(user.Id);
        raised.TelegramUserId.ShouldBe(42L);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_non_positive_telegram_id_is_rejected(long telegramUserId)
    {
        AppUser.Register(telegramUserId, "A", null, null, Now)
            .Error.Code.ShouldBe(IdentityErrors.TelegramIdInvalid);
    }

    [Fact]
    public void A_blank_first_name_is_rejected()
    {
        AppUser.Register(42L, "   ", null, null, Now).Error.Code.ShouldBe(IdentityErrors.FirstNameRequired);
    }

    [Fact]
    public void A_complete_profile_is_accepted()
    {
        var profile = AthleteProfile.Create(
            birthYear: 1996,
            bodyweight: Weight.Kg(74.5m),
            preferredUnit: WeightUnit.Kilograms,
            loadingIncrement: WeightIncrement.DefaultKg,
            experienceLevel: ExperienceLevel.Intermediate,
            availableDaysPerWeek: 3,
            sessionMinutes: 90,
            timeZoneId: "Asia/Tashkent").Value;

        var user = AppUser.Register(42L, "A", null, "uz", Now).Value;
        user.UpdateProfile(profile).IsSuccess.ShouldBeTrue();

        user.Profile.BirthYear.ShouldBe(1996);
        user.Profile.Bodyweight!.Value.ShouldBe(74.5m);
        user.Profile.AvailableDaysPerWeek.ShouldBe(3);
        user.Profile.SessionMinutes.ShouldBe(90);
        user.Profile.TimeZoneId.ShouldBe("Asia/Tashkent");
    }

    [Theory]
    [InlineData(1919)]
    [InlineData(2021)]
    public void An_implausible_birth_year_is_rejected(int birthYear)
    {
        Profile(birthYear: birthYear).Error.Code.ShouldBe(IdentityErrors.BirthYearOutOfBounds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void Available_days_outside_one_to_seven_are_rejected(int days)
    {
        Profile(days: days).Error.Code.ShouldBe(IdentityErrors.AvailableDaysOutOfBounds);
    }

    [Theory]
    [InlineData(19)]
    [InlineData(241)]
    public void Session_length_outside_twenty_to_two_hundred_forty_minutes_is_rejected(int minutes)
    {
        Profile(minutes: minutes).Error.Code.ShouldBe(IdentityErrors.SessionMinutesOutOfBounds);
    }

    [Fact]
    public void An_unknown_time_zone_is_rejected()
    {
        Profile(timeZoneId: "Mars/Olympus").Error.Code.ShouldBe(IdentityErrors.TimeZoneUnknown);
    }

    [Fact]
    public void A_profile_with_all_optional_fields_null_is_accepted()
    {
        Profile(birthYear: null, days: null, minutes: null).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, 3, 90)]
    [InlineData(1996, null, 90)]
    [InlineData(1996, 3, null)]
    public void A_profile_with_exactly_one_optional_field_null_is_accepted(int? birthYear, int? days, int? minutes)
    {
        Profile(birthYear: birthYear, days: days, minutes: minutes).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Touching_last_seen_moves_it_forward_only()
    {
        var user = AppUser.Register(42L, "A", null, null, Now).Value;

        user.TouchLastSeen(Now.AddHours(5));
        user.LastSeenAt.ShouldBe(Now.AddHours(5));

        user.TouchLastSeen(Now.AddHours(1));
        user.LastSeenAt.ShouldBe(Now.AddHours(5));
    }

    private static Result<AthleteProfile> Profile(
        int? birthYear = 1996, int? days = 3, int? minutes = 90, string timeZoneId = "UTC") =>
        AthleteProfile.Create(
            birthYear, Weight.Kg(74.5m), WeightUnit.Kilograms, WeightIncrement.DefaultKg,
            ExperienceLevel.Intermediate, days, minutes, timeZoneId);
}
