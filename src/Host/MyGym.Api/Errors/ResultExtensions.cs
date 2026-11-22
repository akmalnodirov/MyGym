using MyGym.SharedKernel;

namespace MyGym.Api.Errors;

public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : Problem(result.Error);

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null) =>
        result.IsSuccess
            ? onSuccess?.Invoke(result.Value) ?? Results.Ok(result.Value)
            : Problem(result.Error);

    private static IResult Problem(Error error)
    {
        var status = ProblemDetailsMapper.StatusFor(error.Code);

        return Results.Problem(
            title: error.Message,
            statusCode: status,
            type: $"https://mygym.app/errors/{error.Code}",
            extensions: new Dictionary<string, object?> { ["errorCode"] = error.Code });
    }
}
