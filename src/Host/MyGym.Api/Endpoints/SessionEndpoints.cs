using MyGym.Api.Auth;
using MyGym.Api.Errors;
using MyGym.Modules.Training.Application.Sessions;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Api.Endpoints;

public static class SessionEndpoints
{
    public sealed record StartSessionRequest(Guid? ProgramId, Guid? ProgramDayId, decimal? Bodyweight);
    public sealed record LogSetRequest(Guid ClientSetId, SetKind Kind, decimal Weight, int Reps, int? Rir);
    public sealed record EditSetRequest(decimal Weight, int Reps, int? Rir);

    public sealed record AddExerciseRequest(
        Guid ExerciseId, int WorkingSetCount, int RepMin, int RepMax,
        int RestMinSeconds, int RestMaxSeconds);

    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions").RequireAuthorization();

        group.MapPost("", async (
            StartSessionRequest request,
            ICurrentUser me,
            ICommandHandler<StartSessionCommand, SessionResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new StartSessionCommand(
                me.UserId,
                request.ProgramId is { } p ? ProgramId.From(p) : null,
                request.ProgramDayId is { } d ? ProgramDayId.From(d) : null,
                request.Bodyweight), ct);

            return result.ToHttpResult(session => Results.Created($"/api/sessions/{session.Id}", session));
        });

        group.MapGet("/current", async (
            ICurrentUser me,
            IQueryHandler<GetCurrentSessionQuery, SessionResponse?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCurrentSessionQuery(me.UserId), ct);
            return result.ToHttpResult(session =>
                session is null ? Results.NoContent() : Results.Ok(session));
        });

        group.MapGet("", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize,
            ICurrentUser me,
            IQueryHandler<ListSessionsQuery, IReadOnlyList<SessionSummaryResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new ListSessionsQuery(me.UserId, from, to, page ?? 1, pageSize ?? 20), ct);

            return result.ToHttpResult();
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            ICurrentUser me,
            IQueryHandler<GetSessionQuery, SessionResponse> handler,
            CancellationToken ct) =>
            (await handler.HandleAsync(new GetSessionQuery(me.UserId, SessionId.From(id)), ct))
                .ToHttpResult());

        group.MapPost("/{id:guid}/exercises", async (
            Guid id,
            AddExerciseRequest request,
            ICurrentUser me,
            ICommandHandler<AddAdHocExerciseCommand, AddedExerciseResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new AddAdHocExerciseCommand(
                me.UserId, SessionId.From(id), ExerciseId.From(request.ExerciseId),
                request.WorkingSetCount, request.RepMin, request.RepMax,
                request.RestMinSeconds, request.RestMaxSeconds), ct);

            return result.ToHttpResult(added => Results.Created(
                $"/api/sessions/{id}/exercises/{added.SessionExerciseId}", added));
        });

        group.MapPost("/{id:guid}/exercises/{exerciseId:guid}/sets", async (
            Guid id,
            Guid exerciseId,
            LogSetRequest request,
            ICurrentUser me,
            ICommandHandler<LogSetCommand, LoggedSetResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new LogSetCommand(
                me.UserId, SessionId.From(id), SessionExerciseId.From(exerciseId),
                request.ClientSetId, request.Kind, request.Weight, request.Reps, request.Rir), ct);

            return result.ToHttpResult(set => Results.Created(
                $"/api/sessions/{id}/exercises/{exerciseId}/sets/{set.Id}", set));
        });

        group.MapPatch("/{id:guid}/exercises/{exerciseId:guid}/sets/{setId:guid}", async (
            Guid id, Guid exerciseId, Guid setId, EditSetRequest request,
            ICurrentUser me, ICommandHandler<EditSetCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new EditSetCommand(
                me.UserId, SessionId.From(id), SessionExerciseId.From(exerciseId),
                LoggedSetId.From(setId), request.Weight, request.Reps, request.Rir), ct))
                .ToHttpResult());

        group.MapDelete("/{id:guid}/exercises/{exerciseId:guid}/sets/{setId:guid}", async (
            Guid id, Guid exerciseId, Guid setId,
            ICurrentUser me, ICommandHandler<DeleteSetCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DeleteSetCommand(
                me.UserId, SessionId.From(id), SessionExerciseId.From(exerciseId),
                LoggedSetId.From(setId)), ct))
                .ToHttpResult());

        group.MapPost("/{id:guid}/complete", async (
            Guid id, ICurrentUser me, ICommandHandler<CompleteSessionCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new CompleteSessionCommand(me.UserId, SessionId.From(id)), ct))
                .ToHttpResult());

        group.MapPost("/{id:guid}/abandon", async (
            Guid id, ICurrentUser me, ICommandHandler<AbandonSessionCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AbandonSessionCommand(me.UserId, SessionId.From(id)), ct))
                .ToHttpResult());
    }
}
