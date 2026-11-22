namespace MyGym.Modules.Training.Domain;

public static class TrainingErrors
{
    // Value objects
    public const string RepRangeInverted = "rep_range.inverted";
    public const string RepRangeOutOfBounds = "rep_range.out_of_bounds";
    public const string RestPeriodInverted = "rest_period.inverted";
    public const string RestPeriodOutOfBounds = "rest_period.out_of_bounds";
    public const string WarmupPercentNotAscending = "warmup.percent_not_ascending";
    public const string WarmupPercentOutOfBounds = "warmup.percent_out_of_bounds";
    public const string WarmupRepsOutOfBounds = "warmup.reps_out_of_bounds";
    public const string WarmupTooManySteps = "warmup.too_many_steps";

    // Programs
    public const string ProgramNotFound = "program.not_found";
    public const string ProgramNameRequired = "program.name_required";
    public const string ProgramDayLabelRequired = "program.day_label_required";
    public const string ProgramNotEditable = "program.not_editable";
    public const string ProgramNeedsDay = "program.needs_at_least_one_day";
    public const string ProgramDayNeedsPrescription = "program.day_needs_at_least_one_prescription";
    public const string ProgramAlreadyActive = "program.already_active";
    public const string ProgramArchived = "program.archived";
    public const string ProgramDayNotFound = "program.day_not_found";
    public const string PrescriptionWorkingSetsOutOfBounds = "prescription.working_sets_out_of_bounds";
    public const string PrescriptionAlternativeSameAsPrimary = "prescription.alternative_same_as_primary";

    // Sessions
    public const string SessionNotFound = "session.not_found";
    public const string SessionAlreadyInProgress = "session.already_in_progress";
    public const string SessionClosed = "session.closed";
    public const string SessionNeedsWorkingSet = "session.needs_at_least_one_working_set";
    public const string SessionExerciseNotFound = "session.exercise_not_found";
    public const string SessionNeedsExercise = "session.needs_at_least_one_exercise";
    public const string SetNotFound = "set.not_found";
    public const string SetRepsOutOfBounds = "set.reps_out_of_bounds";
    public const string SetRirOutOfBounds = "set.rir_out_of_bounds";
    public const string SetKindInvalid = "set.kind_invalid";
}
