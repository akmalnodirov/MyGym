using MyGym.Modules.Identity.Contracts;
using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Sessions;

public sealed record StartSessionCommand(
    UserId Actor, ProgramId? ProgramId, ProgramDayId? ProgramDayId, decimal? Bodyweight);

public sealed class StartSessionCommandHandler(
    IWorkoutSessionRepository sessions,
    ITrainingProgramRepository programs,
    ILastWorkingWeightQuery lastWeights,
    IAthleteProfileQueries profiles,
    ITrainingUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<StartSessionCommand, SessionResponse>
{
    public async Task<Result<SessionResponse>> HandleAsync(StartSessionCommand command, CancellationToken ct)
    {
        // Application-level guard for the cross-aggregate invariant. The partial unique index in the
        // database is the real guarantee under concurrency; this gives a clean 409 instead of a 500.
        var open = await sessions.GetInProgressForUserAsync(command.Actor, ct);
        if (open is not null)
            return Result<SessionResponse>.Failure(new Error(
                TrainingErrors.SessionAlreadyInProgress, "Finish or abandon the open session first."));

        var preferencesResult = await LoadingPreferencesLookup.ResolveAsync(profiles, command.Actor, ct);
        if (preferencesResult.IsFailure)
            return Result<SessionResponse>.Failure(preferencesResult.Error);

        var preferences = preferencesResult.Value;

        var increment = WeightIncrement.Create(preferences.Increment, preferences.PreferredUnit).Value;

        var origin = SessionOrigin.AdHoc;
        var snapshots = new List<PrescriptionSnapshot>();

        if (command.ProgramId is { } programId)
        {
            if (command.ProgramDayId is not { } dayId)
                return Result<SessionResponse>.Failure(new Error(
                    TrainingErrors.ProgramDayNotFound, "A program day is required when starting from a program."));

            var program = await programs.GetByIdAsync(programId, ct);

            // I9: visibility now also requires Active status for a non-owned program — previously
            // this let an athlete start a session from someone else's Draft or Archived template,
            // bypassing the activation gate entirely.
            var visible = program is not null
                && ProgramVisibilityPolicy.IsVisibleTo(program, command.Actor, ProgramStatus.Active);

            if (!visible)
                return Result<SessionResponse>.Failure(
                    new Error(TrainingErrors.ProgramNotFound, "Program not found."));

            var day = program!.Days.SingleOrDefault(d => d.Id == dayId);
            if (day is null)
                return Result<SessionResponse>.Failure(
                    new Error(TrainingErrors.ProgramDayNotFound, "Day not found."));

            foreach (var prescription in day.Prescriptions)
            {
                var snapshot = PrescriptionSnapshot.Create(
                    prescription.ExerciseId, prescription.AlternativeExerciseId,
                    prescription.WorkingSetCount, prescription.TargetReps, prescription.Rest,
                    prescription.Warmup, prescription.Notes);

                if (snapshot.IsFailure)
                    return Result<SessionResponse>.Failure(snapshot.Error);

                snapshots.Add(snapshot.Value);
            }

            origin = SessionOrigin.FromProgram(programId, program.RevisionNumber, dayId);
        }

        var started = WorkoutSession.Start(command.Actor, origin, snapshots, timeProvider.GetUtcNow());
        if (started.IsFailure)
            return Result<SessionResponse>.Failure(started.Error);

        var session = started.Value;

        if (command.Bodyweight is { } bodyweight)
        {
            var weight = Weight.Create(bodyweight, preferences.PreferredUnit);
            if (weight.IsFailure)
                return Result<SessionResponse>.Failure(weight.Error);

            session.RecordBodyweight(weight.Value);
        }

        await sessions.AddAsync(session, ct);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (SessionAlreadyInProgressConflictException)
        {
            // I2: the application-level check above (GetInProgressForUserAsync) is not safe under
            // concurrency on its own — two requests can both pass it before either has inserted.
            // The database's partial unique index is the real guarantee; this turns its violation
            // into the same 409 the check above already returns for the sequential case.
            return Result<SessionResponse>.Failure(new Error(
                TrainingErrors.SessionAlreadyInProgress, "Finish or abandon the open session first."));
        }

        var suggested = await lastWeights.GetTopWorkingWeightsAsync(
            command.Actor, snapshots.Select(s => s.ExerciseId).ToList(), ct);

        return Result<SessionResponse>.Success(
            SessionMapper.ToResponse(session, suggested, increment, preferences.PreferredUnit));
    }
}
