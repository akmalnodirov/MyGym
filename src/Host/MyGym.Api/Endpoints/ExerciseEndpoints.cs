using MyGym.Modules.ExerciseCatalog.Contracts;

namespace MyGym.Api.Endpoints;

public static class ExerciseEndpoints
{
    public static void MapExerciseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/exercises", async (
                string? q,
                string? lang,
                IExerciseCatalogQueries catalog,
                CancellationToken ct) =>
            Results.Ok(await catalog.SearchAsync(q, lang ?? "en", ct)))
            .RequireAuthorization();
    }
}
