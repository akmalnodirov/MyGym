namespace MyGym.Modules.ExerciseCatalog.Domain;

public enum MovementPattern
{
    HorizontalPush = 1,
    VerticalPush = 2,
    HorizontalPull = 3,
    VerticalPull = 4,
    Squat = 5,
    Hinge = 6,
    Lunge = 7,
    Carry = 8,
    Isolation = 9,
    Core = 10
}

public enum MuscleGroup
{
    Chest = 1,
    Back = 2,
    Lats = 3,
    Shoulders = 4,
    Biceps = 5,
    Triceps = 6,
    Quads = 7,
    Hamstrings = 8,
    Glutes = 9,
    Calves = 10,
    Abs = 11,
    Forearms = 12,
    Traps = 13
}

public enum Equipment
{
    Barbell = 1,
    Dumbbell = 2,
    Machine = 3,
    Cable = 4,
    Bodyweight = 5,
    EzBar = 6,
    Other = 7
}
