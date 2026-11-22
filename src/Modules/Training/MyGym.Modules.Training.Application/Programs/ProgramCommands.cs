using MyGym.Modules.ExerciseCatalog.Contracts;
using MyGym.Modules.Identity.Contracts;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Programs;

public sealed record CreateProgramCommand(UserId Owner, string Name, IReadOnlyList<ProgramDayInput> Days);

public sealed class CreateProgramCommandHandler(
    ITrainingProgramRepository programs,
    IExerciseCatalogQueries catalog,
    IAthleteProfileQueries profiles,
    ITrainingUnitOfWork unitOfWork) : ICommandHandler<CreateProgramCommand, ProgramResponse>
{
    public async Task<Result<ProgramResponse>> HandleAsync(CreateProgramCommand command, CancellationToken ct)
    {
        // C3 (sixth site) — see LoadingPreferencesLookup.EnsureUserExistsAsync's doc comment.
        var exists = await LoadingPreferencesLookup.EnsureUserExistsAsync(profiles, command.Owner, ct);
        if (exists.IsFailure)
            return Result<ProgramResponse>.Failure(exists.Error);

        var built = await ProgramBuilder.BuildAsync(
            command.Owner, command.Name, command.Days, catalog, ct);

        if (built.IsFailure)
            return Result<ProgramResponse>.Failure(built.Error);

        await programs.AddAsync(built.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<ProgramResponse>.Success(ProgramResponse.From(built.Value));
    }
}

public sealed record ReplaceProgramCommand(
    UserId Actor, ProgramId ProgramId, string Name, IReadOnlyList<ProgramDayInput> Days);

/// <summary>
/// Replaces a program's structure wholesale. The athlete edits one form in the Mini App, so a
/// single replace is both simpler and safer than a patch language over days and prescriptions —
/// and already-logged sessions are unaffected because they carry their own snapshot.
/// </summary>
public sealed class ReplaceProgramCommandHandler(
    ITrainingProgramRepository programs,
    IExerciseCatalogQueries catalog,
    ITrainingUnitOfWork unitOfWork) : ICommandHandler<ReplaceProgramCommand, ProgramResponse>
{
    public async Task<Result<ProgramResponse>> HandleAsync(ReplaceProgramCommand command, CancellationToken ct)
    {
        var existing = await programs.GetByIdAsync(command.ProgramId, ct);

        // Not-found rather than forbidden: a program the actor doesn't own gets the same response
        // as one that doesn't exist at all, so nobody can probe which program ids are real by
        // watching for a 403-vs-404 split.
        if (existing is null || existing.OwnerUserId != command.Actor)
            return Result<ProgramResponse>.Failure(
                new Error(TrainingErrors.ProgramNotFound, "Program not found."));

        if (existing.Status == ProgramStatus.Archived)
            return Result<ProgramResponse>.Failure(
                new Error(TrainingErrors.ProgramNotEditable, "An archived program cannot be edited."));

        // I9: invariant 5 ("a program must have at least one day... to be activated") was only
        // checked in Activate(), so a PUT with an empty days array could degrade an already-Active
        // program into a shape the domain declares invalid — a shape POST /api/sessions then fails
        // against with program.day_not_found instead of this being rejected at the edit itself.
        if (command.Days.Count == 0)
            return Result<ProgramResponse>.Failure(
                new Error(TrainingErrors.ProgramNeedsDay, "A program must have at least one day."));

        var rename = existing.Rename(command.Name);
        if (rename.IsFailure)
            return Result<ProgramResponse>.Failure(rename.Error);

        // Days returns a freshly-ordered copy of the backing field, so removing while iterating
        // this is safe — it is not the live collection. Iterating the backing field directly here
        // would mutate the list out from under its own enumerator.
        foreach (var day in existing.Days)
        {
            var removed = existing.RemoveDay(day.Id);
            if (removed.IsFailure)
                return Result<ProgramResponse>.Failure(removed.Error);
        }

        var exercisesExist = await ProgramBuilder.EnsureExercisesExistAsync(command.Days, catalog, ct);
        if (exercisesExist.IsFailure)
            return Result<ProgramResponse>.Failure(exercisesExist.Error);

        foreach (var dayInput in command.Days)
        {
            var day = existing.AddDay(dayInput.Label, dayInput.DayOfWeek);
            if (day.IsFailure)
                return Result<ProgramResponse>.Failure(day.Error);

            foreach (var prescriptionInput in dayInput.Prescriptions)
            {
                var applied = ProgramBuilder.ApplyPrescription(existing, day.Value.Id, prescriptionInput);
                if (applied.IsFailure)
                    return Result<ProgramResponse>.Failure(applied.Error);
            }
        }

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ProgramEditConflictException)
        {
            // I2: RevisionNumber is an optimistic concurrency token (TrainingProgramConfiguration)
            // specifically so a concurrent edit throws here instead of silently overwriting another
            // edit — but nothing previously caught the resulting DbUpdateConcurrencyException, so
            // the configured guard's only outcome was an unmapped 500.
            return Result<ProgramResponse>.Failure(new Error(
                TrainingErrors.ProgramEditConflict, "This program was edited concurrently. Reload and try again."));
        }

        return Result<ProgramResponse>.Success(ProgramResponse.From(existing));
    }
}

public sealed record ActivateProgramCommand(UserId Actor, ProgramId ProgramId);

public sealed class ActivateProgramCommandHandler(
    ITrainingProgramRepository programs, ITrainingUnitOfWork unitOfWork)
    : ICommandHandler<ActivateProgramCommand>
{
    public async Task<Result> HandleAsync(ActivateProgramCommand command, CancellationToken ct)
    {
        var program = await programs.GetByIdAsync(command.ProgramId, ct);
        if (program is null || program.OwnerUserId != command.Actor)
            return Result.Failure(new Error(TrainingErrors.ProgramNotFound, "Program not found."));

        var activated = program.Activate();
        if (activated.IsFailure)
            return activated;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record ArchiveProgramCommand(UserId Actor, ProgramId ProgramId);

public sealed class ArchiveProgramCommandHandler(
    ITrainingProgramRepository programs, ITrainingUnitOfWork unitOfWork)
    : ICommandHandler<ArchiveProgramCommand>
{
    public async Task<Result> HandleAsync(ArchiveProgramCommand command, CancellationToken ct)
    {
        var program = await programs.GetByIdAsync(command.ProgramId, ct);
        if (program is null || program.OwnerUserId != command.Actor)
            return Result.Failure(new Error(TrainingErrors.ProgramNotFound, "Program not found."));

        var archived = program.Archive();
        if (archived.IsFailure)
            return archived;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record SetProgramVisibilityCommand(UserId Actor, ProgramId ProgramId, ProgramVisibility Visibility);

public sealed class SetProgramVisibilityCommandHandler(
    ITrainingProgramRepository programs, ITrainingUnitOfWork unitOfWork)
    : ICommandHandler<SetProgramVisibilityCommand>
{
    public async Task<Result> HandleAsync(SetProgramVisibilityCommand command, CancellationToken ct)
    {
        var program = await programs.GetByIdAsync(command.ProgramId, ct);
        if (program is null || program.OwnerUserId != command.Actor)
            return Result.Failure(new Error(TrainingErrors.ProgramNotFound, "Program not found."));

        var set = program.SetVisibility(command.Visibility);
        if (set.IsFailure)
            return set;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record ForkProgramCommand(UserId Actor, ProgramId ProgramId);

public sealed class ForkProgramCommandHandler(
    ITrainingProgramRepository programs, IAthleteProfileQueries profiles, ITrainingUnitOfWork unitOfWork)
    : ICommandHandler<ForkProgramCommand, ProgramResponse>
{
    public async Task<Result<ProgramResponse>> HandleAsync(ForkProgramCommand command, CancellationToken ct)
    {
        // C3 residual (re-review): Fork is the one write path, of the nine with no deleted-user
        // check, that is actually reachable after deletion rather than unreachable-by-404. The
        // other eight all load-by-id-plus-owner first, and the purge deletes every program and
        // session the actor owned, so those loads already return null post-deletion. Fork instead
        // forks *someone else's* public Active program into a brand-new program owned by the
        // (deleted) actor — exactly the orphaned-row shape C3 and the purge-first ordering exist
        // to prevent. Same mechanism as the other six sites.
        var exists = await LoadingPreferencesLookup.EnsureUserExistsAsync(profiles, command.Actor, ct);
        if (exists.IsFailure)
            return Result<ProgramResponse>.Failure(exists.Error);

        var source = await programs.GetByIdAsync(command.ProgramId, ct);

        // A program is forkable if the actor owns it, or it is public and active (I9).
        var visible = source is not null
            && ProgramVisibilityPolicy.IsVisibleTo(source, command.Actor, ProgramStatus.Active);

        if (!visible)
            return Result<ProgramResponse>.Failure(
                new Error(TrainingErrors.ProgramNotFound, "Program not found."));

        var fork = source!.Fork(command.Actor);
        await programs.AddAsync(fork, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<ProgramResponse>.Success(ProgramResponse.From(fork));
    }
}
