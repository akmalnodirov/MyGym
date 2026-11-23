using System.ComponentModel.DataAnnotations;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Programs;

// ── Inputs ────────────────────────────────────────────────────────────────
public sealed record WarmupStepInput(decimal PercentOfWorking, int Reps, bool IsOptional);

// C4: Warmup is required (not null) so a null step list fails validation at the boundary instead
// of reaching WarmupRamp.Create as null and throwing from the NullReferenceException that follows.
public sealed record PrescriptionInput(
    Guid ExerciseId,
    Guid? AlternativeExerciseId,
    int WorkingSetCount,
    int RepMin,
    int RepMax,
    int RestMinSeconds,
    int RestMaxSeconds,
    [Required] IReadOnlyList<WarmupStepInput> Warmup,
    string? Notes);

public sealed record ProgramDayInput(
    [Required] string Label,
    DayOfWeek? DayOfWeek,
    [Required] IReadOnlyList<PrescriptionInput> Prescriptions);

// ── Outputs ───────────────────────────────────────────────────────────────
public sealed record WarmupStepResponse(decimal PercentOfWorking, int Reps, bool IsOptional);

public sealed record PrescriptionResponse(
    Guid Id, int Order, Guid ExerciseId, Guid? AlternativeExerciseId, int WorkingSetCount,
    int RepMin, int RepMax, int RestMinSeconds, int RestMaxSeconds,
    IReadOnlyList<WarmupStepResponse> Warmup, string? Notes)
{
    public static PrescriptionResponse From(ExercisePrescription p) => new(
        p.Id.Value, p.Order, p.ExerciseId.Value, p.AlternativeExerciseId?.Value, p.WorkingSetCount,
        p.TargetReps.Min, p.TargetReps.Max, (int)p.Rest.Min.TotalSeconds, (int)p.Rest.Max.TotalSeconds,
        p.Warmup.Steps.Select(s => new WarmupStepResponse(s.PercentOfWorking, s.Reps, s.IsOptional)).ToList(),
        p.Notes);
}

public sealed record ProgramDayResponse(
    Guid Id, int Order, string Label, DayOfWeek? DayOfWeek, IReadOnlyList<PrescriptionResponse> Prescriptions)
{
    public static ProgramDayResponse From(ProgramDay d) => new(
        d.Id.Value, d.Order, d.Label, d.DayOfWeek,
        d.Prescriptions.Select(PrescriptionResponse.From).ToList());
}

public sealed record ProgramResponse(
    Guid Id, string Name, ProgramStatus Status, ProgramVisibility Visibility, int RevisionNumber,
    Guid? ForkedFromProgramId, IReadOnlyList<ProgramDayResponse> Days)
{
    public static ProgramResponse From(TrainingProgram p) => new(
        p.Id.Value, p.Name, p.Status, p.Visibility, p.RevisionNumber,
        p.ForkedFromProgramId?.Value, p.Days.Select(ProgramDayResponse.From).ToList());
}

public sealed record ProgramSummaryResponse(
    Guid Id, string Name, ProgramStatus Status, ProgramVisibility Visibility, int DayCount,
    bool IsOwnedByMe);

public sealed record ResolvedWarmupResponse(decimal Weight, int Reps, bool IsOptional);

public sealed record PreviewExerciseResponse(
    Guid ExerciseId, Guid? AlternativeExerciseId, int WorkingSetCount, int TargetRepMin,
    int TargetRepMax, int RestMinSeconds, int RestMaxSeconds, decimal? SuggestedWorkingWeight,
    WeightUnit WeightUnit, IReadOnlyList<ResolvedWarmupResponse> Warmup, string? Notes);

public sealed record DayPreviewResponse(
    Guid ProgramId, Guid DayId, string Label, int RevisionNumber,
    IReadOnlyList<PreviewExerciseResponse> Exercises);
