using MyGym.Modules.ExerciseCatalog.Contracts;
using MyGym.Modules.Identity.Contracts;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Sessions;

// RULING R1 (Task 13): no SessionErrors class here. TrainingErrors.SessionNotFound (Training.Domain)
// is already the one definition of "session.not_found" — Task 11's error mapper was written against
// it — so every handler below uses that constant directly instead of redeclaring the same string in
// a second class. See Task 12's ProgramErrors precedent for why a second source of truth for one wire
// contract is the thing being avoided.

public sealed record LogSetCommand(
    UserId Actor, SessionId SessionId, SessionExerciseId SessionExerciseId, Guid ClientSetId,
    SetKind Kind, decimal Weight, int Reps, int? Rir);

public sealed class LogSetCommandHandler(
    IWorkoutSessionRepository sessions,
    IAthleteProfileQueries profiles,
    ITrainingUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<LogSetCommand, LoggedSetResponse>
{
    public async Task<Result<LoggedSetResponse>> HandleAsync(LogSetCommand command, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.Actor)
            return Result<LoggedSetResponse>.Failure(new Error(TrainingErrors.SessionNotFound, "Session not found."));

        var preferences = await profiles.GetLoadingPreferencesAsync(command.Actor, ct)
                          ?? new LoadingPreferences(WeightUnit.Kilograms, 2.5m);

        var weight = Weight.Create(command.Weight, preferences.PreferredUnit);
        if (weight.IsFailure)
            return Result<LoggedSetResponse>.Failure(weight.Error);

        var logged = session.LogSet(
            command.SessionExerciseId, command.ClientSetId, command.Kind, weight.Value,
            command.Reps, command.Rir, timeProvider.GetUtcNow());

        if (logged.IsFailure)
            return Result<LoggedSetResponse>.Failure(logged.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result<LoggedSetResponse>.Success(LoggedSetResponse.From(logged.Value));
    }
}

public sealed record EditSetCommand(
    UserId Actor, SessionId SessionId, SessionExerciseId SessionExerciseId, LoggedSetId SetId,
    decimal Weight, int Reps, int? Rir);

public sealed class EditSetCommandHandler(
    IWorkoutSessionRepository sessions,
    IAthleteProfileQueries profiles,
    ITrainingUnitOfWork unitOfWork) : ICommandHandler<EditSetCommand>
{
    public async Task<Result> HandleAsync(EditSetCommand command, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.Actor)
            return Result.Failure(new Error(TrainingErrors.SessionNotFound, "Session not found."));

        var preferences = await profiles.GetLoadingPreferencesAsync(command.Actor, ct)
                          ?? new LoadingPreferences(WeightUnit.Kilograms, 2.5m);

        var weight = Weight.Create(command.Weight, preferences.PreferredUnit);
        if (weight.IsFailure)
            return Result.Failure(weight.Error);

        var edited = session.EditSet(
            command.SessionExerciseId, command.SetId, weight.Value, command.Reps, command.Rir);

        if (edited.IsFailure)
            return edited;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record DeleteSetCommand(
    UserId Actor, SessionId SessionId, SessionExerciseId SessionExerciseId, LoggedSetId SetId);

public sealed class DeleteSetCommandHandler(
    IWorkoutSessionRepository sessions, ITrainingUnitOfWork unitOfWork) : ICommandHandler<DeleteSetCommand>
{
    public async Task<Result> HandleAsync(DeleteSetCommand command, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.Actor)
            return Result.Failure(new Error(TrainingErrors.SessionNotFound, "Session not found."));

        var deleted = session.DeleteSet(command.SessionExerciseId, command.SetId);
        if (deleted.IsFailure)
            return deleted;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record AddedExerciseResponse(Guid SessionExerciseId);

public sealed record AddAdHocExerciseCommand(
    UserId Actor, SessionId SessionId, ExerciseId ExerciseId, int WorkingSetCount,
    int RepMin, int RepMax, int RestMinSeconds, int RestMaxSeconds);

/// <summary>
/// Adds an exercise to a session in progress — the ad-hoc path, and also the escape hatch when a
/// machine is occupied and the athlete substitutes something mid-workout. The program is untouched.
/// </summary>
public sealed class AddAdHocExerciseCommandHandler(
    IWorkoutSessionRepository sessions,
    IExerciseCatalogQueries catalog,
    ITrainingUnitOfWork unitOfWork) : ICommandHandler<AddAdHocExerciseCommand, AddedExerciseResponse>
{
    public async Task<Result<AddedExerciseResponse>> HandleAsync(
        AddAdHocExerciseCommand command, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.Actor)
            return Result<AddedExerciseResponse>.Failure(
                new Error(TrainingErrors.SessionNotFound, "Session not found."));

        // RULING R26 (Task 13): the one definition of this wire-contract code is
        // ExerciseCatalogErrors.ExerciseNotFound in ExerciseCatalog.Contracts (Task 12 created it so
        // Training.Application could reference the catalog's error without referencing its Domain).
        // No second "exercise.not_found" string literal here.
        if (!await catalog.AllExistAsync([command.ExerciseId], ct))
            return Result<AddedExerciseResponse>.Failure(
                new Error(ExerciseCatalogErrors.ExerciseNotFound, "Exercise does not exist."));

        var reps = Domain.ValueObjects.RepRange.Create(command.RepMin, command.RepMax);
        if (reps.IsFailure)
            return Result<AddedExerciseResponse>.Failure(reps.Error);

        var rest = Domain.ValueObjects.RestPeriod.Create(
            TimeSpan.FromSeconds(command.RestMinSeconds), TimeSpan.FromSeconds(command.RestMaxSeconds));
        if (rest.IsFailure)
            return Result<AddedExerciseResponse>.Failure(rest.Error);

        // An ad-hoc exercise has no prescribed ramp: the athlete warms up by feel.
        var snapshot = Domain.ValueObjects.PrescriptionSnapshot.Create(
            command.ExerciseId, null, command.WorkingSetCount, reps.Value, rest.Value,
            Domain.ValueObjects.WarmupRamp.Empty, null);

        if (snapshot.IsFailure)
            return Result<AddedExerciseResponse>.Failure(snapshot.Error);

        var added = session.AddExercise(snapshot.Value);
        if (added.IsFailure)
            return Result<AddedExerciseResponse>.Failure(added.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result<AddedExerciseResponse>.Success(new AddedExerciseResponse(added.Value.Id.Value));
    }
}

public sealed record CompleteSessionCommand(UserId Actor, SessionId SessionId);

public sealed class CompleteSessionCommandHandler(
    IWorkoutSessionRepository sessions, ITrainingUnitOfWork unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<CompleteSessionCommand>
{
    public async Task<Result> HandleAsync(CompleteSessionCommand command, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.Actor)
            return Result.Failure(new Error(TrainingErrors.SessionNotFound, "Session not found."));

        var completed = session.Complete(timeProvider.GetUtcNow());
        if (completed.IsFailure)
            return completed;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record AbandonSessionCommand(UserId Actor, SessionId SessionId);

public sealed class AbandonSessionCommandHandler(
    IWorkoutSessionRepository sessions, ITrainingUnitOfWork unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<AbandonSessionCommand>
{
    public async Task<Result> HandleAsync(AbandonSessionCommand command, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.Actor)
            return Result.Failure(new Error(TrainingErrors.SessionNotFound, "Session not found."));

        var abandoned = session.Abandon(timeProvider.GetUtcNow());
        if (abandoned.IsFailure)
            return abandoned;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
