using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyGym.Modules.Training.Domain.Sessions;

namespace MyGym.Modules.Training.Infrastructure;

/// <summary>
/// Same reasoning as <see cref="SessionExerciseConfiguration"/>: <see cref="LoggedSet.Weight"/> is a
/// required value object bound through LoggedSet's constructor, which an EF Core owned-entity
/// navigation cannot accept — even with Weight registered as a complex type model-wide. Mapping
/// LoggedSet as a regular dependent entity type is the only way found to keep Weight as a
/// constructor-bound value+unit column pair here, which the task requires (the progress engine
/// aggregates on weight numerically, so it must never be a serialized blob).
/// </summary>
internal sealed class LoggedSetConfiguration : IEntityTypeConfiguration<LoggedSet>
{
    public const string ClientSetIdIndexName = "ix_logged_sets_session_exercise_id_client_set_id";

    public void Configure(EntityTypeBuilder<LoggedSet> builder)
    {
        builder.ToTable("logged_sets");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Order).HasColumnName("order");
        builder.Property(s => s.Kind).HasConversion<int>();

        builder.HasOne<SessionExercise>()
            .WithMany("_sets")
            .HasForeignKey(s => s.SessionExerciseId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // Weight is registered as a complex type model-wide in TrainingDbContext.ConfigureConventions;
        // here we only customize its column names.
        builder.ComplexProperty(s => s.Weight, weight =>
        {
            weight.Property(w => w.Value).HasColumnName("weight_value").HasPrecision(7, 2);
            weight.Property(w => w.Unit).HasColumnName("weight_unit").HasConversion<int>();
        });

        // Invariant: logging a set is idempotent on the client-generated id.
        builder.HasIndex(s => new { s.SessionExerciseId, s.ClientSetId })
            .HasDatabaseName(ClientSetIdIndexName)
            .IsUnique();
    }
}
