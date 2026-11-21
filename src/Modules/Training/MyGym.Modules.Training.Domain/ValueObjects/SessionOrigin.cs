using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.ValueObjects;

public sealed record SessionOrigin(ProgramId? ProgramId, int? ProgramRevision, ProgramDayId? ProgramDayId)
{
    public static SessionOrigin AdHoc { get; } = new(null, null, null);

    public static SessionOrigin FromProgram(ProgramId programId, int revision, ProgramDayId dayId) =>
        new(programId, revision, dayId);

    public bool IsAdHoc => ProgramId is null;
}
