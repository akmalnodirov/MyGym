using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyGym.Modules.Training.Domain.Sessions;

namespace MyGym.Modules.Training.Infrastructure;

/// <summary>
/// SessionExercise belongs to the WorkoutSession aggregate and is never queried on its own — but it
/// cannot be configured as an EF Core "owned" type (<c>OwnsMany</c>): its <see cref="SessionExercise.Prescription"/>
/// is a required value object bound through the constructor, and EF Core will not bind a constructor
/// parameter to anything nested inside an owned entity, even when the CLR type is registered as a
/// complex type model-wide. Modelling SessionExercise as a regular dependent entity type instead —
/// same table, same required+cascade relationship an owned type would have had, same AutoInclude —
/// gives the identical physical shape and load behaviour while allowing Prescription to be a complex
/// (constructor-bound) property.
/// </summary>
internal sealed class SessionExerciseConfiguration : IEntityTypeConfiguration<SessionExercise>
{
    public void Configure(EntityTypeBuilder<SessionExercise> builder)
    {
        builder.ToTable("session_exercises");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Order).HasColumnName("order");
        builder.Ignore(e => e.ExerciseId); // derived from the snapshot

        // Sets is a public, read-only computed property that recomputes an ordered List from the
        // private "_sets" field on every access; same reasoning as WorkoutSession.Exercises for why
        // it must be ignored rather than left for EF to auto-discover as a second navigation.
        builder.Ignore(e => e.Sets);

        builder.HasIndex(e => new { e.SessionId, e.Order }).IsUnique();

        builder.HasOne<WorkoutSession>()
            .WithMany("_exercises")
            .HasForeignKey(e => e.SessionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // PrescriptionSnapshot is registered as a complex type model-wide in
        // TrainingDbContext.ConfigureConventions; here we only customize its column names.
        builder.ComplexProperty(e => e.Prescription, snapshot =>
        {
            snapshot.Property(p => p.ExerciseId).HasColumnName("exercise_id");
            snapshot.Property(p => p.AlternativeExerciseId).HasColumnName("alternative_exercise_id");
            snapshot.Property(p => p.WorkingSetCount).HasColumnName("working_set_count");
            snapshot.Property(p => p.TargetReps).HasColumnName("target_reps");
            snapshot.Property(p => p.Rest).HasColumnName("rest");
            snapshot.Property(p => p.Warmup).HasColumnName("warmup").HasColumnType("jsonb");
            snapshot.Property(p => p.Notes).HasColumnName("notes").HasMaxLength(500);
        });

        builder.Navigation("_sets").AutoInclude();
    }
}
