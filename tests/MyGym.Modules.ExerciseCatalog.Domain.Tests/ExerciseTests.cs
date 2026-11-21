using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.Modules.ExerciseCatalog.Domain.Tests;

public class ExerciseTests
{
    private static Result<Exercise> Create(string slug = "incline-dumbbell-press") =>
        Exercise.Create(
            slug,
            MovementPattern.HorizontalPush,
            [MuscleGroup.Chest],
            [MuscleGroup.Shoulders, MuscleGroup.Triceps],
            Equipment.Dumbbell,
            [ExerciseName.Create("en", "Incline Dumbbell Press").Value,
             ExerciseName.Create("uz", "Qiya gantel press").Value],
            ["incline db press", "incline bench dumbbell"]);

    [Fact]
    public void A_catalog_exercise_is_created_with_its_names_and_muscles()
    {
        var exercise = Create().Value;

        exercise.Slug.ShouldBe("incline-dumbbell-press");
        exercise.IsCustom.ShouldBeFalse();
        exercise.OwnerUserId.ShouldBeNull();
        exercise.PrimaryMuscles.ShouldBe([MuscleGroup.Chest]);
        exercise.Names.Count.ShouldBe(2);
        exercise.Aliases.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("Incline Dumbbell Press")]
    [InlineData("incline_dumbbell_press")]
    [InlineData("incline dumbbell press")]
    [InlineData("")]
    public void A_slug_must_be_lowercase_kebab_case(string slug)
    {
        Create(slug).Error.Code.ShouldBe(CatalogErrors.SlugInvalid);
    }

    [Fact]
    public void An_exercise_needs_at_least_one_name()
    {
        var result = Exercise.Create(
            "x", MovementPattern.Isolation, [MuscleGroup.Biceps], [], Equipment.Dumbbell, [], []);

        result.Error.Code.ShouldBe(CatalogErrors.NameRequired);
    }

    [Fact]
    public void An_exercise_needs_at_least_one_primary_muscle()
    {
        var result = Exercise.Create(
            "x", MovementPattern.Isolation, [], [], Equipment.Dumbbell,
            [ExerciseName.Create("en", "X").Value], []);

        result.Error.Code.ShouldBe(CatalogErrors.PrimaryMuscleRequired);
    }

    [Fact]
    public void Duplicate_language_codes_are_rejected()
    {
        var result = Exercise.Create(
            "x", MovementPattern.Isolation, [MuscleGroup.Biceps], [], Equipment.Dumbbell,
            [ExerciseName.Create("en", "X").Value, ExerciseName.Create("en", "Y").Value], []);

        result.Error.Code.ShouldBe(CatalogErrors.DuplicateLanguage);
    }

    [Fact]
    public void Language_codes_are_normalised_to_lowercase()
    {
        ExerciseName.Create("EN", "Squat").Value.LanguageCode.ShouldBe("en");
    }

    [Fact]
    public void A_two_letter_language_code_is_required()
    {
        ExerciseName.Create("english", "Squat").Error.Code.ShouldBe(CatalogErrors.LanguageCodeInvalid);
    }

    [Fact]
    public void Name_lookup_falls_back_to_english_then_to_the_first_name()
    {
        var exercise = Create().Value;

        exercise.NameFor("uz").ShouldBe("Qiya gantel press");
        exercise.NameFor("ru").ShouldBe("Incline Dumbbell Press");
    }

    [Fact]
    public void A_custom_exercise_belongs_to_its_creator()
    {
        var owner = UserId.New();

        var exercise = Exercise.CreateCustom(
            owner, "my-weird-machine", MovementPattern.Isolation, [MuscleGroup.Calves], [],
            Equipment.Machine, [ExerciseName.Create("en", "My Weird Machine").Value]).Value;

        exercise.IsCustom.ShouldBeTrue();
        exercise.OwnerUserId.ShouldBe(owner);
    }

    [Fact]
    public void Aliases_are_normalised_to_lowercase_and_trimmed()
    {
        var exercise = Exercise.Create(
            "x", MovementPattern.Isolation, [MuscleGroup.Biceps], [], Equipment.Dumbbell,
            [ExerciseName.Create("en", "X").Value],
            [" Incline DB Press ", "INCLINE BENCH DUMBBELL"]).Value;

        exercise.Aliases.ShouldBe(["incline db press", "incline bench dumbbell"]);
    }

    [Fact]
    public void Duplicate_primary_and_secondary_muscles_are_deduplicated()
    {
        var exercise = Exercise.Create(
            "x", MovementPattern.Isolation,
            [MuscleGroup.Chest, MuscleGroup.Chest],
            [MuscleGroup.Triceps, MuscleGroup.Triceps, MuscleGroup.Shoulders],
            Equipment.Dumbbell, [ExerciseName.Create("en", "X").Value], []).Value;

        exercise.PrimaryMuscles.ShouldBe([MuscleGroup.Chest]);
        exercise.SecondaryMuscles.ShouldBe([MuscleGroup.Triceps, MuscleGroup.Shoulders]);
    }
}
