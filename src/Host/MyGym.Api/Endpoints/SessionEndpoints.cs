using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
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

    // C4: Kind is bound with a global JsonStringEnumConverter that (by default) also accepts the
    // raw underlying int, so {"kind": 99} used to deserialise straight into an undefined SetKind
    // that CountWorkingSets() and Complete's guard both silently ignore. EnumDataType rejects it at
    // the boundary with a 400/422 instead.
    public sealed record LogSetRequest(
        Guid ClientSetId, [EnumDataType(typeof(SetKind))] SetKind Kind, decimal Weight, int Reps, int? Rir);

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
        }).WithMetadata(new RequestSizeLimitAttribute(4 * 1024));

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

            // Minor 4: there is no GET /api/sessions/{id}/exercises/{seId} route — the only way to
            // read a session exercise back is as part of the whole session — so the Location
            // header pointed at a route that 404s. Point it at the session itself, which is the
            // nearest resource that actually resolves and already contains the new exercise.
            return result.ToHttpResult(added => Results.Created($"/api/sessions/{id}", added));
        }).WithMetadata(new RequestSizeLimitAttribute(4 * 1024));

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
        }).WithMetadata(new RequestSizeLimitAttribute(2 * 1024));

        group.MapPatch("/{id:guid}/exercises/{exerciseId:guid}/sets/{setId:guid}", async (
            Guid id, Guid exerciseId, Guid setId, EditSetRequest request,
            ICurrentUser me, ICommandHandler<EditSetCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new EditSetCommand(
                me.UserId, SessionId.From(id), SessionExerciseId.From(exerciseId),
                LoggedSetId.From(setId), request.Weight, request.Reps, request.Rir), ct))
                .ToHttpResult())
            .WithMetadata(new RequestSizeLimitAttribute(2 * 1024));

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
