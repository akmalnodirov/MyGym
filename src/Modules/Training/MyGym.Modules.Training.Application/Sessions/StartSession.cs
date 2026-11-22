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

        var preferences = await profiles.GetLoadingPreferencesAsync(command.Actor, ct)
                          ?? new LoadingPreferences(WeightUnit.Kilograms, 2.5m);

        var increment = WeightIncrement.Create(preferences.Increment, preferences.PreferredUnit).Value;

        var origin = SessionOrigin.AdHoc;
        var snapshots = new List<PrescriptionSnapshot>();

        if (command.ProgramId is { } programId)
        {
            if (command.ProgramDayId is not { } dayId)
                return Result<SessionResponse>.Failure(new Error(
                    TrainingErrors.ProgramDayNotFound, "A program day is required when starting from a program."));

            var program = await programs.GetByIdAsync(programId, ct);
            var visible = program is not null
                && (program.OwnerUserId == command.Actor || program.Visibility == ProgramVisibility.Public);

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
        await unitOfWork.SaveChangesAsync(ct);

        var suggested = await lastWeights.GetTopWorkingWeightsAsync(
            command.Actor, snapshots.Select(s => s.ExerciseId).ToList(), ct);

        return Result<SessionResponse>.Success(
            SessionMapper.ToResponse(session, suggested, increment, preferences.PreferredUnit));
    }
}
