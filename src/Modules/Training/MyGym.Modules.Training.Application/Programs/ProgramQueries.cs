using MyGym.Modules.Identity.Contracts;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Programs;

public sealed record GetProgramQuery(UserId Actor, ProgramId ProgramId);

public sealed class GetProgramQueryHandler(ITrainingProgramRepository programs)
    : IQueryHandler<GetProgramQuery, ProgramResponse>
{
    public async Task<Result<ProgramResponse>> HandleAsync(GetProgramQuery query, CancellationToken ct)
    {
        var program = await programs.GetByIdAsync(query.ProgramId, ct);

        var visible = program is not null
            && (program.OwnerUserId == query.Actor || program.Visibility == ProgramVisibility.Public);

        // Not-found rather than forbidden: a program the actor may not see returns the same
        // response as one that does not exist, so nobody can probe which program ids are real.
        return visible
            ? Result<ProgramResponse>.Success(ProgramResponse.From(program!))
            : Result<ProgramResponse>.Failure(new Error(TrainingErrors.ProgramNotFound, "Program not found."));
    }
}

/// <summary>Handler lives in Infrastructure — it is a read-side projection, not a domain operation.</summary>
public sealed record ListProgramsQuery(UserId Actor);

public sealed record PreviewProgramDayQuery(UserId Actor, ProgramId ProgramId, ProgramDayId DayId);

public sealed class PreviewProgramDayQueryHandler(
    ITrainingProgramRepository programs,
    ILastWorkingWeightQuery lastWeights,
    IAthleteProfileQueries profiles) : IQueryHandler<PreviewProgramDayQuery, DayPreviewResponse>
{
    public async Task<Result<DayPreviewResponse>> HandleAsync(
        PreviewProgramDayQuery query, CancellationToken ct)
    {
        var program = await programs.GetByIdAsync(query.ProgramId, ct);
        var visible = program is not null
            && (program.OwnerUserId == query.Actor || program.Visibility == ProgramVisibility.Public);

        // Not-found rather than forbidden: same reasoning as GetProgramQueryHandler above.
        if (!visible)
            return Result<DayPreviewResponse>.Failure(
                new Error(TrainingErrors.ProgramNotFound, "Program not found."));

        var day = program!.Days.SingleOrDefault(d => d.Id == query.DayId);
        if (day is null)
            return Result<DayPreviewResponse>.Failure(
                new Error(TrainingErrors.ProgramDayNotFound, "Day not found."));

        var preferencesResult = await LoadingPreferencesLookup.ResolveAsync(profiles, query.Actor, ct);
        if (preferencesResult.IsFailure)
            return Result<DayPreviewResponse>.Failure(preferencesResult.Error);

        var preferences = preferencesResult.Value;

        var increment = WeightIncrement.Create(preferences.Increment, preferences.PreferredUnit).Value;

        var exerciseIds = day.Prescriptions.Select(p => p.ExerciseId).ToList();
        var history = await lastWeights.GetTopWorkingWeightsAsync(query.Actor, exerciseIds, ct);

        var exercises = day.Prescriptions.Select(prescription =>
        {
            history.TryGetValue(prescription.ExerciseId, out var lastWorking);

            // C1: lastWorking carries the unit it was logged in, which may differ from the
            // athlete's current preference reported as WeightUnit below. Normalise first, or the
            // response claims a kg number is pounds (or throws resolving the warm-up ramp).
            var normalizedLastWorking = SuggestedWeightPresentation.ConvertOrNull(
                lastWorking, preferences.PreferredUnit);

            // No history yet: resolve against zero so the shape is stable and the Mini App can
            // prompt for a starting weight. Auto-progression is sub-project 2's job.
            var working = normalizedLastWorking ?? Weight.Create(0m, preferences.PreferredUnit).Value;
            var resolved = prescription.Warmup.Resolve(working, increment);

            return new PreviewExerciseResponse(
                prescription.ExerciseId.Value,
                prescription.AlternativeExerciseId?.Value,
                prescription.WorkingSetCount,
                prescription.TargetReps.Min,
                prescription.TargetReps.Max,
                (int)prescription.Rest.Min.TotalSeconds,
                (int)prescription.Rest.Max.TotalSeconds,
                normalizedLastWorking?.Value,
                preferences.PreferredUnit.ToString(),
                resolved.Select(r => new ResolvedWarmupResponse(r.Weight.Value, r.Reps, r.IsOptional)).ToList(),
                prescription.Notes);
        }).ToList();

        return Result<DayPreviewResponse>.Success(new DayPreviewResponse(
            program.Id.Value, day.Id.Value, day.Label, program.RevisionNumber, exercises));
    }
}
