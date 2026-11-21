namespace MyGym.SharedKernel;

public interface ICommandHandler<in TCommand, TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct);
}

public interface ICommandHandler<in TCommand>
{
    Task<Result> HandleAsync(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken ct);
}
