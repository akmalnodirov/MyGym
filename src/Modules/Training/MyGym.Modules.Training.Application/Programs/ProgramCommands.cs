using MyGym.Modules.ExerciseCatalog.Contracts;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Programs;

public sealed record CreateProgramCommand(UserId Owner, string Name, IReadOnlyList<ProgramDayInput> Days);

public sealed class CreateProgramCommandHandler(
    ITrainingProgramRepository programs,
    IExerciseCatalogQueries catalog,
    ITrainingUnitOfWork unitOfWork) : ICommandHandler<CreateProgramCommand, ProgramResponse>
{
    public async Task<Result<ProgramResponse>> HandleAsync(CreateProgramCommand command, CancellationToken ct)
    {
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

        await unitOfWork.SaveChangesAsync(ct);
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
    ITrainingProgramRepository programs, ITrainingUnitOfWork unitOfWork)
    : ICommandHandler<ForkProgramCommand, ProgramResponse>
{
    public async Task<Result<ProgramResponse>> HandleAsync(ForkProgramCommand command, CancellationToken ct)
    {
        var source = await programs.GetByIdAsync(command.ProgramId, ct);

        // A program is forkable if the actor owns it, or it is public.
        var visible = source is not null
            && (source.OwnerUserId == command.Actor || source.Visibility == ProgramVisibility.Public);

        if (!visible)
            return Result<ProgramResponse>.Failure(
                new Error(TrainingErrors.ProgramNotFound, "Program not found."));

        var fork = source!.Fork(command.Actor);
        await programs.AddAsync(fork, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<ProgramResponse>.Success(ProgramResponse.From(fork));
    }
}
