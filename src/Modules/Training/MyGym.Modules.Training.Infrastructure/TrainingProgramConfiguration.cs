using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyGym.Modules.Training.Domain.Programs;

namespace MyGym.Modules.Training.Infrastructure;

internal sealed class TrainingProgramConfiguration : IEntityTypeConfiguration<TrainingProgram>
{
    public void Configure(EntityTypeBuilder<TrainingProgram> builder)
    {
        builder.ToTable("programs");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(TrainingProgram.MaxNameLength).IsRequired();
        builder.Property(p => p.Status).HasConversion<int>();
        builder.Property(p => p.Visibility).HasConversion<int>();
        builder.Property(p => p.RevisionNumber).IsConcurrencyToken();
        builder.HasIndex(p => p.OwnerUserId);

        // Days and prescriptions are owned: they load with the root automatically, which is exactly
        // aggregate semantics and removes any chance of evaluating invariants on a half-loaded graph.
        //
        // Each level also exposes a public, read-only computed property (Days, Prescriptions) that
        // recomputes an ordered List from the private backing field on every access. EF's convention
        // discovers that computed property as a SECOND, independent navigation to the same related
        // type, alongside the one explicitly configured here via the "_days"/"_prescriptions" field —
        // and once both are owned, EF throws because it cannot tell which one actually owns the
        // relationship. Ignoring the computed property removes it from the model without touching the
        // domain type; application code keeps using Days/Prescriptions exactly as before.
        builder.Ignore(p => p.Days);

        builder.OwnsMany<ProgramDay>("_days", day =>
        {
            day.ToTable("program_days");
            day.WithOwner().HasForeignKey(d => d.ProgramId);
            day.HasKey(d => d.Id);
            day.Property(d => d.Label).HasMaxLength(120).IsRequired();
            day.Property(d => d.Order).HasColumnName("order");
            day.Ignore(d => d.Prescriptions); // see note above
            day.HasIndex(d => new { d.ProgramId, d.Order }).IsUnique();

            day.OwnsMany<ExercisePrescription>("_prescriptions", prescription =>
            {
                prescription.ToTable("exercise_prescriptions");
                prescription.WithOwner().HasForeignKey(p => p.ProgramDayId);
                prescription.HasKey(p => p.Id);
                prescription.Property(p => p.Order).HasColumnName("order");
                prescription.Property(p => p.Notes).HasMaxLength(500);
                prescription.HasIndex(p => new { p.ProgramDayId, p.Order }).IsUnique();
            });
        });

        builder.Navigation("_days").AutoInclude();
    }
}
