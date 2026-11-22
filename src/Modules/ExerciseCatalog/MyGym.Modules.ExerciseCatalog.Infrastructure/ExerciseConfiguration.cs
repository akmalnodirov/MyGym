using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyGym.Modules.ExerciseCatalog.Domain;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

internal sealed class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> builder)
    {
        builder.ToTable("exercises");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Slug).HasMaxLength(80).IsRequired();
        builder.HasIndex(e => e.Slug).IsUnique();
        builder.Property(e => e.MovementPattern).HasConversion<int>();
        builder.Property(e => e.Equipment).HasConversion<int>();
        builder.Ignore(e => e.IsCustom);

        // Identity and ExerciseCatalog are separate schemas with no cross-schema foreign keys, so
        // OwnerUserId is a bare indexed column, never a navigation/FK into the identity schema.
        builder.HasIndex(e => e.OwnerUserId);

        // Enum lists and aliases are stored as PostgreSQL arrays: queryable with ANY(), no join.
        builder.Property(e => e.PrimaryMuscles)
            .HasConversion(
                muscles => muscles.Select(m => (int)m).ToArray(),
                values => values.Select(v => (MuscleGroup)v).ToList())
            .HasColumnType("integer[]")
            .Metadata.SetValueComparer(
                new ValueComparer<IReadOnlyList<MuscleGroup>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (hash, m) => HashCode.Combine(hash, m)),
                    list => list.ToList()));

        builder.Property(e => e.SecondaryMuscles)
            .HasConversion(
                muscles => muscles.Select(m => (int)m).ToArray(),
                values => values.Select(v => (MuscleGroup)v).ToList())
            .HasColumnType("integer[]")
            .Metadata.SetValueComparer(
                new ValueComparer<IReadOnlyList<MuscleGroup>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (hash, m) => HashCode.Combine(hash, m)),
                    list => list.ToList()));

        // RULING R3: Names and Aliases must be configured BY EXPRESSION, not by string field name.
        // Both are deliberately plain pass-throughs to their backing fields (unlike the Training
        // module's ordered-copy accessors), which makes this possible — and it is required, because
        // Task 12 filters the catalog in SQL via `e.Names.Any(...)` / `e.Aliases.Any(...)`. A
        // string-named field navigation is not reachable from that LINQ and would throw at
        // translation time.
        builder.PrimitiveCollection(e => e.Aliases)
            .HasColumnName("aliases")
            .HasColumnType("text[]");

        builder.OwnsMany(e => e.Names, name =>
        {
            name.ToTable("exercise_names");
            name.WithOwner().HasForeignKey("exercise_id");
            name.Property(n => n.LanguageCode).HasColumnName("language_code").HasMaxLength(2).IsRequired();
            name.Property(n => n.Value).HasColumnName("value").HasMaxLength(120).IsRequired();
            name.HasKey("exercise_id", nameof(ExerciseName.LanguageCode));
        });

        builder.Navigation(e => e.Names).AutoInclude();
    }
}
