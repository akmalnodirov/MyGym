using MyGym.Api.Auth;
using MyGym.Api.Errors;
using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Api.Endpoints;

public static class ProgramEndpoints
{
    public sealed record SaveProgramRequest(string Name, IReadOnlyList<ProgramDayInput> Days);
    public sealed record SetVisibilityRequest(ProgramVisibility Visibility);

    public static void MapProgramEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/programs").RequireAuthorization();

        group.MapPost("", async (
            SaveProgramRequest request,
            ICurrentUser me,
            ICommandHandler<CreateProgramCommand, ProgramResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new CreateProgramCommand(me.UserId, request.Name, request.Days), ct);

            return result.ToHttpResult(program => Results.Created($"/api/programs/{program.Id}", program));
        });

        group.MapGet("", async (
            ICurrentUser me,
            IQueryHandler<ListProgramsQuery, IReadOnlyList<ProgramSummaryResponse>> handler,
            CancellationToken ct) =>
            (await handler.HandleAsync(new ListProgramsQuery(me.UserId), ct)).ToHttpResult());

        group.MapGet("/{id:guid}", async (
            Guid id,
            ICurrentUser me,
            IQueryHandler<GetProgramQuery, ProgramResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProgramQuery(me.UserId, ProgramId.From(id)), ct);
            return result.ToHttpResult();
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            SaveProgramRequest request,
            ICurrentUser me,
            ICommandHandler<ReplaceProgramCommand, ProgramResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new ReplaceProgramCommand(me.UserId, ProgramId.From(id), request.Name, request.Days), ct);

            return result.ToHttpResult();
        });

        group.MapPost("/{id:guid}/activate", async (
            Guid id, ICurrentUser me, ICommandHandler<ActivateProgramCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ActivateProgramCommand(me.UserId, ProgramId.From(id)), ct))
                .ToHttpResult());

        group.MapDelete("/{id:guid}", async (
            Guid id, ICurrentUser me, ICommandHandler<ArchiveProgramCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ArchiveProgramCommand(me.UserId, ProgramId.From(id)), ct))
                .ToHttpResult());

        group.MapPatch("/{id:guid}/visibility", async (
            Guid id,
            SetVisibilityRequest request,
            ICurrentUser me,
            ICommandHandler<SetProgramVisibilityCommand> handler,
            CancellationToken ct) =>
            (await handler.HandleAsync(
                new SetProgramVisibilityCommand(me.UserId, ProgramId.From(id), request.Visibility), ct))
                .ToHttpResult());

        group.MapPost("/{id:guid}/fork", async (
            Guid id,
            ICurrentUser me,
            ICommandHandler<ForkProgramCommand, ProgramResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ForkProgramCommand(me.UserId, ProgramId.From(id)), ct);
            return result.ToHttpResult(fork => Results.Created($"/api/programs/{fork.Id}", fork));
        });

        group.MapGet("/{id:guid}/days/{dayId:guid}/preview", async (
            Guid id,
            Guid dayId,
            ICurrentUser me,
            IQueryHandler<PreviewProgramDayQuery, DayPreviewResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new PreviewProgramDayQuery(me.UserId, ProgramId.From(id), ProgramDayId.From(dayId)), ct);

            return result.ToHttpResult();
        });
    }
}
