using Microsoft.EntityFrameworkCore;
using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.Modules.ExerciseCatalog.Infrastructure;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class CatalogPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Seeding_inserts_the_whole_catalog()
    {
        await using var context = fixture.CreateCatalogContext();
        await CatalogSeeder.SeedAsync(context, CancellationToken.None);

        (await context.Exercises.CountAsync(TestContext.Current.CancellationToken))
            .ShouldBeGreaterThanOrEqualTo(26);
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_exercises()
    {
        await using var context = fixture.CreateCatalogContext();
        await CatalogSeeder.SeedAsync(context, CancellationToken.None);
        var afterFirst = await context.Exercises.CountAsync(TestContext.Current.CancellationToken);

        await CatalogSeeder.SeedAsync(context, CancellationToken.None);

        (await context.Exercises.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(afterFirst);
    }

    [Fact]
    public async Task A_seeded_exercise_round_trips_its_names_muscles_and_aliases()
    {
        await using var context = fixture.CreateCatalogContext();
        await CatalogSeeder.SeedAsync(context, CancellationToken.None);

        var squat = await context.Exercises.AsNoTracking()
            .SingleAsync(e => e.Slug == "back-squat", TestContext.Current.CancellationToken);

        squat.MovementPattern.ShouldBe(MovementPattern.Squat);
        squat.Equipment.ShouldBe(Equipment.Barbell);
        squat.PrimaryMuscles.ShouldContain(MuscleGroup.Quads);
        squat.SecondaryMuscles.ShouldContain(MuscleGroup.Glutes);
        squat.NameFor("uz").ShouldBe("Shtanga bilan o'tirib turish");
        squat.NameFor("en").ShouldBe("Back Squat");
        squat.Aliases.ShouldContain("squat");
    }

    [Fact]
    public async Task Slugs_are_unique_in_the_database()
    {
        await using var context = fixture.CreateCatalogContext();
        await CatalogSeeder.SeedAsync(context, CancellationToken.None);

        context.Exercises.Add(Exercise.Create(
            "back-squat", MovementPattern.Squat, [MuscleGroup.Quads], [], Equipment.Barbell,
            [ExerciseName.Create("en", "Duplicate").Value], []).Value);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        exception.InnerException!.Message.ShouldContain("slug");
    }
}
