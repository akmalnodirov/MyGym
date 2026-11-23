using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using MyGym.Api.Auth;
using MyGym.Api.Errors;
using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Api.Endpoints;

public static class ProgramEndpoints
{
    // C4: Days is marked required so a missing or null "days" key fails .NET 10's minimal-API
    // validation (via AddValidation() in Program.cs) with a 400/422 instead of reaching
    // ProgramBuilder.BuildAsync as a null and throwing a NullReferenceException from
    // days.SelectMany(...) — the exact reproduction in the review.
    public sealed record SaveProgramRequest(
        [Required] string Name, [Required] IReadOnlyList<ProgramDayInput> Days);

    public sealed record SetVisibilityRequest([EnumDataType(typeof(ProgramVisibility))] ProgramVisibility Visibility);

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
        }).Produces<ProgramResponse>(StatusCodes.Status201Created)
          .WithMetadata(new RequestSizeLimitAttribute(256 * 1024));

        group.MapGet("", async (
            ICurrentUser me,
            IQueryHandler<ListProgramsQuery, IReadOnlyList<ProgramSummaryResponse>> handler,
            CancellationToken ct) =>
            (await handler.HandleAsync(new ListProgramsQuery(me.UserId), ct)).ToHttpResult())
            .Produces<IReadOnlyList<ProgramSummaryResponse>>();

        group.MapGet("/{id:guid}", async (
            Guid id,
            ICurrentUser me,
            IQueryHandler<GetProgramQuery, ProgramResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProgramQuery(me.UserId, ProgramId.From(id)), ct);
            return result.ToHttpResult();
        }).Produces<ProgramResponse>();

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
        }).Produces<ProgramResponse>()
          .WithMetadata(new RequestSizeLimitAttribute(256 * 1024));

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
                .ToHttpResult())
            .WithMetadata(new RequestSizeLimitAttribute(4 * 1024));

        group.MapPost("/{id:guid}/fork", async (
            Guid id,
            ICurrentUser me,
            ICommandHandler<ForkProgramCommand, ProgramResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ForkProgramCommand(me.UserId, ProgramId.From(id)), ct);
            return result.ToHttpResult(fork => Results.Created($"/api/programs/{fork.Id}", fork));
        }).Produces<ProgramResponse>(StatusCodes.Status201Created);

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
        }).Produces<DayPreviewResponse>();
    }
}
