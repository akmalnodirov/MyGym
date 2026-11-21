namespace MyGym.Modules.ExerciseCatalog.Domain;

/// <summary>
/// The starting catalog: every exercise in the author's own 3-day program, so the app is usable
/// on day one. Names are English and Uzbek; Russian can be added without code changes.
/// </summary>
public static class CatalogSeed
{
    private static ExerciseName En(string value) => ExerciseName.Create("en", value).Value;
    private static ExerciseName Uz(string value) => ExerciseName.Create("uz", value).Value;

    private static Exercise Make(
        string slug, MovementPattern pattern, MuscleGroup[] primary, MuscleGroup[] secondary,
        Equipment equipment, string en, string uz, params string[] aliases) =>
        Exercise.Create(slug, pattern, primary, secondary, equipment, [En(en), Uz(uz)], aliases).Value;

    public static IReadOnlyList<Exercise> Exercises { get; } =
    [
        Make("incline-dumbbell-press", MovementPattern.HorizontalPush,
            [MuscleGroup.Chest], [MuscleGroup.Shoulders, MuscleGroup.Triceps], Equipment.Dumbbell,
            "Incline Dumbbell Press", "Qiya gantel press", "incline db press"),
        Make("flat-barbell-bench-press", MovementPattern.HorizontalPush,
            [MuscleGroup.Chest], [MuscleGroup.Shoulders, MuscleGroup.Triceps], Equipment.Barbell,
            "Flat Barbell Bench Press", "Gorizontal shtanga press", "bench press", "bench"),
        Make("machine-chest-press", MovementPattern.HorizontalPush,
            [MuscleGroup.Chest], [MuscleGroup.Triceps], Equipment.Machine,
            "Machine Chest Press", "Trenajyorda ko'krak press"),
        Make("pec-deck", MovementPattern.Isolation,
            [MuscleGroup.Chest], [], Equipment.Machine,
            "Pec Deck", "Pek-dek", "butterfly"),
        Make("incline-cable-fly", MovementPattern.Isolation,
            [MuscleGroup.Chest], [], Equipment.Cable,
            "Incline Cable Fly", "Qiya bloklarda yoyish"),
        Make("chest-supported-row", MovementPattern.HorizontalPull,
            [MuscleGroup.Back], [MuscleGroup.Lats, MuscleGroup.Biceps], Equipment.Machine,
            "Chest Supported Row", "Ko'krak tayanchli tortish"),
        Make("seated-cable-row", MovementPattern.HorizontalPull,
            [MuscleGroup.Back], [MuscleGroup.Lats, MuscleGroup.Biceps], Equipment.Cable,
            "Seated Cable Row", "O'tirgan holda blok tortish"),
        Make("lat-pulldown", MovementPattern.VerticalPull,
            [MuscleGroup.Lats], [MuscleGroup.Biceps], Equipment.Cable,
            "Lat Pulldown", "Yuqori blok tortish"),
        Make("pull-up", MovementPattern.VerticalPull,
            [MuscleGroup.Lats], [MuscleGroup.Biceps], Equipment.Bodyweight,
            "Pull-up", "Turnikda tortilish", "chin up"),
        Make("seated-dumbbell-shoulder-press", MovementPattern.VerticalPush,
            [MuscleGroup.Shoulders], [MuscleGroup.Triceps], Equipment.Dumbbell,
            "Seated Dumbbell Shoulder Press", "O'tirgan holda gantel press"),
        Make("dumbbell-lateral-raise", MovementPattern.Isolation,
            [MuscleGroup.Shoulders], [], Equipment.Dumbbell,
            "Dumbbell Lateral Raise", "Gantelni yonga ko'tarish", "lateral raise", "side raise"),
        Make("rear-delt-fly", MovementPattern.Isolation,
            [MuscleGroup.Shoulders], [MuscleGroup.Traps], Equipment.Dumbbell,
            "Rear Delt Fly", "Orqa delta yoyish"),
        Make("ez-bar-curl", MovementPattern.Isolation,
            [MuscleGroup.Biceps], [MuscleGroup.Forearms], Equipment.EzBar,
            "EZ Bar Curl", "EZ shtanga bitseps"),
        Make("incline-dumbbell-curl", MovementPattern.Isolation,
            [MuscleGroup.Biceps], [], Equipment.Dumbbell,
            "Incline Dumbbell Curl", "Qiya gantel bitseps"),
        Make("rope-triceps-pushdown", MovementPattern.Isolation,
            [MuscleGroup.Triceps], [], Equipment.Cable,
            "Rope Triceps Pushdown", "Arqon bilan tritseps"),
        Make("overhead-ez-bar-triceps-extension", MovementPattern.Isolation,
            [MuscleGroup.Triceps], [], Equipment.EzBar,
            "Overhead EZ-Bar Triceps Extension", "Bosh ustidan EZ tritseps"),
        Make("overhead-rope-triceps-extension", MovementPattern.Isolation,
            [MuscleGroup.Triceps], [], Equipment.Cable,
            "Overhead Rope Triceps Extension", "Bosh ustidan arqon tritseps"),
        Make("back-squat", MovementPattern.Squat,
            [MuscleGroup.Quads], [MuscleGroup.Glutes, MuscleGroup.Hamstrings], Equipment.Barbell,
            "Back Squat", "Shtanga bilan o'tirib turish", "squat"),
        Make("leg-press", MovementPattern.Squat,
            [MuscleGroup.Quads], [MuscleGroup.Glutes], Equipment.Machine,
            "Leg Press", "Oyoq press"),
        Make("bulgarian-split-squat", MovementPattern.Lunge,
            [MuscleGroup.Quads], [MuscleGroup.Glutes], Equipment.Dumbbell,
            "Bulgarian Split Squat", "Bolgar split prisedaniya"),
        Make("walking-lunge", MovementPattern.Lunge,
            [MuscleGroup.Quads], [MuscleGroup.Glutes], Equipment.Dumbbell,
            "Walking Lunge", "Yurib qadamlash"),
        Make("romanian-deadlift", MovementPattern.Hinge,
            [MuscleGroup.Hamstrings], [MuscleGroup.Glutes, MuscleGroup.Back], Equipment.Barbell,
            "Romanian Deadlift", "Rumin tortish", "rdl"),
        Make("leg-curl", MovementPattern.Isolation,
            [MuscleGroup.Hamstrings], [], Equipment.Machine,
            "Leg Curl", "Oyoq bukish"),
        Make("standing-calf-raise", MovementPattern.Isolation,
            [MuscleGroup.Calves], [], Equipment.Machine,
            "Standing Calf Raise", "Tik turib boldir ko'tarish"),
        Make("hanging-leg-raise", MovementPattern.Core,
            [MuscleGroup.Abs], [], Equipment.Bodyweight,
            "Hanging Leg Raise", "Osilib oyoq ko'tarish"),
        Make("cable-crunch", MovementPattern.Core,
            [MuscleGroup.Abs], [], Equipment.Cable,
            "Cable Crunch", "Blokda qorin bukish")
    ];
}
