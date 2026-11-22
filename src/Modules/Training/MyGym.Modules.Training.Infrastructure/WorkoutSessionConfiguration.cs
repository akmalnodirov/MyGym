using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

internal sealed class WorkoutSessionConfiguration : IEntityTypeConfiguration<WorkoutSession>
{
    public const string InProgressIndexName = "ix_sessions_user_id_in_progress";

    public void Configure(EntityTypeBuilder<WorkoutSession> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Status).HasConversion<int>();
        builder.HasIndex(s => new { s.UserId, s.StartedAt });

        // Invariant: at most one in-progress session per athlete. The aggregate cannot see its
        // siblings, so the database is the only place this can be guaranteed under concurrency.
        builder.HasIndex(s => s.UserId)
            .HasDatabaseName(InProgressIndexName)
            .HasFilter($"status = {(int)SessionStatus.InProgress}")
            .IsUnique();

        // SessionOrigin is a value object bound through WorkoutSession's constructor (ruling R16
        // added an EF-materialization-only constructor so this binds at all). It is registered as a
        // complex type model-wide in TrainingDbContext.ConfigureConventions; here we only customize
        // its column names.
        builder.ComplexProperty(s => s.Origin, origin =>
        {
            origin.Property(o => o.ProgramId).HasColumnName("program_id");
            origin.Property(o => o.ProgramRevision).HasColumnName("program_revision");
            origin.Property(o => o.ProgramDayId).HasColumnName("program_day_id");
        });

        builder.ComplexProperty(s => s.BodyweightAtSession, weight =>
        {
            weight.IsRequired(false);
            weight.Property(w => w.Value).HasColumnName("bodyweight_value").HasPrecision(6, 2);
            weight.Property(w => w.Unit).HasColumnName("bodyweight_unit").HasConversion<int>();
        });

        // Exercises is a public, read-only computed property that recomputes an ordered List from
        // the private "_exercises" field on every access. EF's convention discovers it as a SECOND,
        // independent navigation to SessionExercise alongside the one explicitly configured below via
        // the field name, which it cannot reconcile. Ignoring it removes it from the model only;
        // application code keeps using Exercises exactly as before.
        builder.Ignore(s => s.Exercises);

        // SessionExercise cannot be configured as an EF "owned" type (see SessionExerciseConfiguration
        // for why), so the relationship is a regular required, cascading one-to-many into the private
        // "_exercises" field, with AutoInclude standing in for the automatic loading owned types would
        // otherwise have given us for free.
        builder.Navigation("_exercises").AutoInclude();
    }
}
