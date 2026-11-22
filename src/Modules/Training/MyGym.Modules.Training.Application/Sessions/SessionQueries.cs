using MyGym.Modules.Identity.Contracts;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Sessions;

public sealed record GetSessionQuery(UserId Actor, SessionId SessionId);

public sealed class GetSessionQueryHandler(
    IWorkoutSessionRepository sessions,
    ILastWorkingWeightQuery lastWeights,
    IAthleteProfileQueries profiles) : IQueryHandler<GetSessionQuery, SessionResponse>
{
    public async Task<Result<SessionResponse>> HandleAsync(GetSessionQuery query, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(query.SessionId, ct);
        if (session is null || session.UserId != query.Actor)
            return Result<SessionResponse>.Failure(new Error(TrainingErrors.SessionNotFound, "Session not found."));

        return await EnrichAsync(session, query.Actor, lastWeights, profiles, ct);
    }

    internal static async Task<Result<SessionResponse>> EnrichAsync(
        WorkoutSession session,
        UserId actor,
        ILastWorkingWeightQuery lastWeights,
        IAthleteProfileQueries profiles,
        CancellationToken ct)
    {
        var preferencesResult = await LoadingPreferencesLookup.ResolveAsync(profiles, actor, ct);
        if (preferencesResult.IsFailure)
            return Result<SessionResponse>.Failure(preferencesResult.Error);

        var preferences = preferencesResult.Value;

        var increment = WeightIncrement.Create(preferences.Increment, preferences.PreferredUnit).Value;

        var suggested = await lastWeights.GetTopWorkingWeightsAsync(
            actor, session.Exercises.Select(e => e.Prescription.ExerciseId).ToList(), ct);

        return Result<SessionResponse>.Success(
            SessionMapper.ToResponse(session, suggested, increment, preferences.PreferredUnit));
    }
}

public sealed record GetCurrentSessionQuery(UserId Actor);

public sealed class GetCurrentSessionQueryHandler(
    IWorkoutSessionRepository sessions,
    ILastWorkingWeightQuery lastWeights,
    IAthleteProfileQueries profiles) : IQueryHandler<GetCurrentSessionQuery, SessionResponse?>
{
    public async Task<Result<SessionResponse?>> HandleAsync(GetCurrentSessionQuery query, CancellationToken ct)
    {
        var session = await sessions.GetInProgressForUserAsync(query.Actor, ct);
        if (session is null)
            return Result<SessionResponse?>.Success(null);

        var enriched = await GetSessionQueryHandler.EnrichAsync(session, query.Actor, lastWeights, profiles, ct);
        return enriched.IsFailure
            ? Result<SessionResponse?>.Failure(enriched.Error)
            : Result<SessionResponse?>.Success(enriched.Value);
    }
}

public sealed record ListSessionsQuery(
    UserId Actor, DateTimeOffset? From, DateTimeOffset? To, int Page, int PageSize);
