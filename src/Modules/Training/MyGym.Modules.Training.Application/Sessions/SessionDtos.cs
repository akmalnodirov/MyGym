using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Sessions;

// Kind, Unit and Status below are the enums rather than their ToString(). The wire format is
// identical -- JsonStringEnumConverter is registered globally, so they still serialise as
// "Working" / "Kilograms" / "InProgress" -- but the OpenAPI document then describes them as string
// unions instead of bare `string`. The Mini App branches on exactly these three values (read-only
// mode on Status, row grouping on Kind, the unit label on Unit), so a typo in any of them is a
// compile error on the client rather than a silently-false comparison.
public sealed record LoggedSetResponse(
    Guid Id, int Order, SetKind Kind, decimal Weight, WeightUnit Unit, int Reps, int? Rir,
    Guid ClientSetId, DateTimeOffset LoggedAt)
{
    public static LoggedSetResponse From(LoggedSet s) => new(
        s.Id.Value, s.Order, s.Kind, s.Weight.Value, s.Weight.Unit,
        s.Reps, s.Rir, s.ClientSetId, s.LoggedAt);
}

public sealed record SessionExerciseResponse(
    Guid Id, int Order, Guid ExerciseId, Guid? AlternativeExerciseId, int WorkingSetCount,
    int TargetRepMin, int TargetRepMax, int RestMinSeconds, int RestMaxSeconds,
    IReadOnlyList<ResolvedWarmupResponse> SuggestedWarmup, decimal? SuggestedWorkingWeight,
    IReadOnlyList<LoggedSetResponse> Sets);

public sealed record SessionResponse(
    Guid Id, SessionStatus Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    Guid? ProgramId, int? ProgramRevision, Guid? ProgramDayId, decimal? BodyweightAtSession,
    int WorkingSetCount, IReadOnlyList<SessionExerciseResponse> Exercises);

public sealed record SessionSummaryResponse(
    Guid Id, SessionStatus Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int WorkingSetCount);

internal static class SessionMapper
{
    /// <summary>
    /// Maps a session, enriching each exercise with resolved warm-ups and the suggested working
    /// weight so the Mini App needs exactly one request to render the whole workout screen.
    /// </summary>
    public static SessionResponse ToResponse(
        WorkoutSession session,
        IReadOnlyDictionary<ExerciseId, Weight> suggestedWeights,
        WeightIncrement increment,
        WeightUnit unit)
    {
        var exercises = session.Exercises.Select(exercise =>
        {
            suggestedWeights.TryGetValue(exercise.Prescription.ExerciseId, out var suggested);

            // C1: suggested carries the unit it was logged in, which may differ from the
            // athlete's current preference (increment is built in the current unit). Normalise
            // before combining the two, or RoundDownTo throws on a unit mismatch.
            var normalizedSuggested = SuggestedWeightPresentation.ConvertOrNull(suggested, unit);
            var working = normalizedSuggested ?? Weight.Create(0m, unit).Value;
            var resolved = exercise.Prescription.Warmup.Resolve(working, increment);

            return new SessionExerciseResponse(
                exercise.Id.Value,
                exercise.Order,
                exercise.Prescription.ExerciseId.Value,
                exercise.Prescription.AlternativeExerciseId?.Value,
                exercise.Prescription.WorkingSetCount,
                exercise.Prescription.TargetReps.Min,
                exercise.Prescription.TargetReps.Max,
                (int)exercise.Prescription.Rest.Min.TotalSeconds,
                (int)exercise.Prescription.Rest.Max.TotalSeconds,
                resolved.Select(r => new ResolvedWarmupResponse(r.Weight.Value, r.Reps, r.IsOptional)).ToList(),
                normalizedSuggested?.Value,
                exercise.Sets.Select(LoggedSetResponse.From).ToList());
        }).ToList();

        return new SessionResponse(
            session.Id.Value,
            session.Status,
            session.StartedAt,
            session.CompletedAt,
            session.Origin.ProgramId?.Value,
            session.Origin.ProgramRevision,
            session.Origin.ProgramDayId?.Value,
            session.BodyweightAtSession?.Value,
            session.WorkingSetCount,
            exercises);
    }
}
