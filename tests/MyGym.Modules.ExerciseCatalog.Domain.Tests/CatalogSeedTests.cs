using MyGym.Modules.ExerciseCatalog.Domain;
using Shouldly;

namespace MyGym.Modules.ExerciseCatalog.Domain.Tests;

public class CatalogSeedTests
{
    [Fact]
    public void Every_seeded_exercise_is_valid()
    {
        CatalogSeed.Exercises.ShouldNotBeEmpty();
        CatalogSeed.Exercises.Count.ShouldBe(26);
    }

    [Fact]
    public void Seeded_slugs_are_unique()
    {
        CatalogSeed.Exercises.Select(e => e.Slug).Distinct().Count()
            .ShouldBe(CatalogSeed.Exercises.Count);
    }

    [Fact]
    public void Every_seeded_exercise_has_an_english_and_an_uzbek_name()
    {
        foreach (var exercise in CatalogSeed.Exercises)
        {
            exercise.Names.Any(n => n.LanguageCode == "en").ShouldBeTrue(exercise.Slug);
            exercise.Names.Any(n => n.LanguageCode == "uz").ShouldBeTrue(exercise.Slug);
        }
    }

    [Fact]
    public void The_authors_program_exercises_are_all_present()
    {
        string[] required =
        [
            "incline-dumbbell-press", "chest-supported-row", "lat-pulldown", "pull-up",
            "seated-dumbbell-shoulder-press", "machine-chest-press", "dumbbell-lateral-raise",
            "ez-bar-curl", "rope-triceps-pushdown", "back-squat", "romanian-deadlift",
            "leg-press", "walking-lunge", "leg-curl", "standing-calf-raise", "hanging-leg-raise",
            "cable-crunch", "flat-barbell-bench-press", "seated-cable-row", "bulgarian-split-squat",
            "pec-deck", "incline-cable-fly", "rear-delt-fly", "incline-dumbbell-curl",
            "overhead-ez-bar-triceps-extension", "overhead-rope-triceps-extension"
        ];

        var slugs = CatalogSeed.Exercises.Select(e => e.Slug).ToHashSet();
        foreach (var slug in required)
            slugs.ShouldContain(slug);
    }
}
