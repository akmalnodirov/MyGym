using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyGym.Modules.Identity.Domain;

namespace MyGym.Modules.Identity.Infrastructure;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.FirstName).HasMaxLength(120).IsRequired();
        builder.Property(u => u.Username).HasMaxLength(64);
        builder.Property(u => u.LanguageCode).HasMaxLength(8);
        builder.HasIndex(u => u.TelegramUserId).IsUnique();

        builder.OwnsOne(u => u.Profile, profile =>
        {
            profile.Property(p => p.BirthYear).HasColumnName("birth_year");
            profile.Property(p => p.PreferredUnit).HasColumnName("preferred_unit").HasConversion<int>();
            profile.Property(p => p.ExperienceLevel).HasColumnName("experience_level").HasConversion<int>();
            profile.Property(p => p.AvailableDaysPerWeek).HasColumnName("available_days_per_week");
            profile.Property(p => p.SessionMinutes).HasColumnName("session_minutes");
            profile.Property(p => p.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(64).IsRequired();

            profile.OwnsOne(p => p.Bodyweight, weight =>
            {
                weight.Property(w => w.Value).HasColumnName("bodyweight_value").HasPrecision(6, 2);
                weight.Property(w => w.Unit).HasColumnName("bodyweight_unit").HasConversion<int>();
            });

            profile.OwnsOne(p => p.LoadingIncrement, increment =>
            {
                increment.Property(i => i.Value).HasColumnName("loading_increment_value").HasPrecision(5, 2);
                increment.Property(i => i.Unit).HasColumnName("loading_increment_unit").HasConversion<int>();
            });
        });

        builder.Navigation(u => u.Profile).IsRequired();
    }
}
