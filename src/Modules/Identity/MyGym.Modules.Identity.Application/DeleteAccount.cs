using MyGym.Modules.Identity.Domain;
using MyGym.Modules.Training.Contracts;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Application;

public sealed record DeleteAccountCommand(UserId UserId);

public sealed class DeleteAccountCommandHandler(
    IAppUserRepository users,
    IRefreshTokenRepository refreshTokens,
    ITrainingDataPurger trainingData,
    IIdentityUnitOfWork unitOfWork) : ICommandHandler<DeleteAccountCommand>
{
    public async Task<Result> HandleAsync(DeleteAccountCommand command, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null)
            return Result.Failure(new Error(IdentityErrors.UserNotFound, "User not found."));

        // Training data first: if that fails, the account survives and the athlete can retry.
        // The reverse order could orphan sessions with no owner to delete them.
        //
        // This purge and the removal below run against two different DbContexts (Training's and
        // Identity's), so they are two separate transactions — there is no distributed transaction
        // tying them together. PurgeForUserAsync uses ExecuteDeleteAsync, which issues SQL
        // immediately against the training database and commits on its own, outside of this
        // handler's unit of work. If the process crashes or the identity-side SaveChangesAsync
        // below fails after the purge has already committed, the athlete's training data is gone
        // but their account row (and revoked tokens) remain — an orphaned, empty account rather
        // than an orphaned pile of data with no owner. That residual risk is accepted deliberately:
        // the alternative ordering (remove the user first) risks leaving training data with no
        // owner left to delete it, which is strictly worse for a module boundary that cannot use a
        // database-level cascade (no FK crosses the identity/training schema boundary).
        await trainingData.PurgeForUserAsync(command.UserId, ct);

        // I10: a real hard delete, not a revoke — see IRefreshTokenRepository.DeleteAllForUserAsync.
        await refreshTokens.DeleteAllForUserAsync(command.UserId, ct);
        users.Remove(user);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
