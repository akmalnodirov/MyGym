using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Programs;

/// <summary>
/// I9: the "owned || Public" visibility check was duplicated across four handlers —
/// <c>GetProgramQueryHandler</c>, <c>PreviewProgramDayQueryHandler</c>,
/// <c>ForkProgramCommandHandler</c>, and <c>StartSessionCommandHandler</c> — and none of them
/// considered <see cref="TrainingProgram.Status"/>, so a <c>Draft</c> program (never activated) or
/// an <c>Archived</c> one (deliberately retired) was fetchable, previewable, forkable, and
/// startable by anyone as soon as it was marked <see cref="ProgramVisibility.Public"/>, bypassing
/// the activation gate entirely. The owner can always reach their own program regardless of
/// status — only a non-owned program is held to <paramref name="requiredStatus"/>.
/// </summary>
internal static class ProgramVisibilityPolicy
{
    public static bool IsVisibleTo(TrainingProgram program, UserId actor, ProgramStatus requiredStatus) =>
        program.OwnerUserId == actor
        || (program.Visibility == ProgramVisibility.Public && program.Status == requiredStatus);
}
