using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class IdentityPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_user_round_trips_with_a_fully_populated_profile()
    {
        var user = AppUser.Register(Random.Shared.NextInt64(1, long.MaxValue), "Akmal", "akmal", "uz", Now).Value;
        user.UpdateProfile(AthleteProfile.Create(
            1996, Weight.Kg(74.5m), WeightUnit.Kilograms, WeightIncrement.DefaultKg,
            ExperienceLevel.Intermediate, 3, 90, "Asia/Tashkent").Value);

        await using (var write = fixture.CreateIdentityContext())
        {
            write.Users.Add(user);
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = fixture.CreateIdentityContext();
        var loaded = await read.Users.AsNoTracking()
            .SingleAsync(u => u.Id == user.Id, TestContext.Current.CancellationToken);

        loaded.FirstName.ShouldBe("Akmal");
        loaded.LanguageCode.ShouldBe("uz");
        loaded.Profile.BirthYear.ShouldBe(1996);
        loaded.Profile.Bodyweight!.Value.ShouldBe(74.5m);
        loaded.Profile.LoadingIncrement.Value.ShouldBe(2.5m);
        loaded.Profile.ExperienceLevel.ShouldBe(ExperienceLevel.Intermediate);
        loaded.Profile.AvailableDaysPerWeek.ShouldBe(3);
        loaded.Profile.TimeZoneId.ShouldBe("Asia/Tashkent");
    }

    [Fact]
    public async Task A_telegram_account_can_only_be_linked_once()
    {
        var telegramId = Random.Shared.NextInt64(1, long.MaxValue);

        await using var context = fixture.CreateIdentityContext();
        context.Users.Add(AppUser.Register(telegramId, "First", null, null, Now).Value);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Users.Add(AppUser.Register(telegramId, "Second", null, null, Now).Value);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        exception.InnerException!.Message.ShouldContain("telegram_user_id");
    }

    [Fact]
    public async Task A_user_can_be_looked_up_by_telegram_id()
    {
        var telegramId = Random.Shared.NextInt64(1, long.MaxValue);
        var user = AppUser.Register(telegramId, "Akmal", null, "uz", Now).Value;

        await using (var write = fixture.CreateIdentityContext())
        {
            write.Users.Add(user);
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = fixture.CreateIdentityContext();
        var found = await read.Users.SingleOrDefaultAsync(
            u => u.TelegramUserId == telegramId, TestContext.Current.CancellationToken);

        found!.Id.ShouldBe(user.Id);
    }
}
