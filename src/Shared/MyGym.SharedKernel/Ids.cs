namespace MyGym.SharedKernel;

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());
    public static UserId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct ProgramId(Guid Value)
{
    public static ProgramId New() => new(Guid.CreateVersion7());
    public static ProgramId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct ProgramDayId(Guid Value)
{
    public static ProgramDayId New() => new(Guid.CreateVersion7());
    public static ProgramDayId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct PrescriptionId(Guid Value)
{
    public static PrescriptionId New() => new(Guid.CreateVersion7());
    public static PrescriptionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct SessionId(Guid Value)
{
    public static SessionId New() => new(Guid.CreateVersion7());
    public static SessionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct SessionExerciseId(Guid Value)
{
    public static SessionExerciseId New() => new(Guid.CreateVersion7());
    public static SessionExerciseId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct LoggedSetId(Guid Value)
{
    public static LoggedSetId New() => new(Guid.CreateVersion7());
    public static LoggedSetId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct ExerciseId(Guid Value)
{
    public static ExerciseId New() => new(Guid.CreateVersion7());
    public static ExerciseId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
