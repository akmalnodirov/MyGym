using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Sessions;

public sealed record LoggedSetResponse(
    Guid Id, int Order, string Kind, decimal Weight, string Unit, int Reps, int? Rir,
    Guid ClientSetId, DateTimeOffset LoggedAt)
{
    public static LoggedSetResponse From(LoggedSet s) => new(
        s.Id.Value, s.Order, s.Kind.ToString(), s.Weight.Value, s.Weight.Unit.ToString(),
        s.Reps, s.Rir, s.ClientSetId, s.LoggedAt);
}

public sealed record SessionExerciseResponse(
    Guid Id, int Order, Guid ExerciseId, Guid? AlternativeExerciseId, int WorkingSetCount,
    int TargetRepMin, int TargetRepMax, int RestMinSeconds, int RestMaxSeconds,
    IReadOnlyList<ResolvedWarmupResponse> SuggestedWarmup, decimal? SuggestedWorkingWeight,
    IReadOnlyList<LoggedSetResponse> Sets);

public sealed record SessionResponse(
    Guid Id, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    Guid? ProgramId, int? ProgramRevision, Guid? ProgramDayId, decimal? BodyweightAtSession,
    int WorkingSetCount, IReadOnlyList<SessionExerciseResponse> Exercises);

public sealed record SessionSummaryResponse(
    Guid Id, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int WorkingSetCount);

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
            var working = suggested ?? Weight.Create(0m, unit).Value;
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
                suggested?.Value,
                exercise.Sets.Select(LoggedSetResponse.From).ToList());
        }).ToList();

        return new SessionResponse(
            session.Id.Value,
            session.Status.ToString(),
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
