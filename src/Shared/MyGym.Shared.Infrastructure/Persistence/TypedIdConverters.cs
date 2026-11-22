using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MyGym.SharedKernel;

namespace MyGym.Shared.Infrastructure.Persistence;

public sealed class UserIdConverter() : ValueConverter<UserId, Guid>(id => id.Value, v => UserId.From(v));
public sealed class ProgramIdConverter() : ValueConverter<ProgramId, Guid>(id => id.Value, v => ProgramId.From(v));
public sealed class ProgramDayIdConverter() : ValueConverter<ProgramDayId, Guid>(id => id.Value, v => ProgramDayId.From(v));
public sealed class PrescriptionIdConverter() : ValueConverter<PrescriptionId, Guid>(id => id.Value, v => PrescriptionId.From(v));
public sealed class SessionIdConverter() : ValueConverter<SessionId, Guid>(id => id.Value, v => SessionId.From(v));
public sealed class SessionExerciseIdConverter() : ValueConverter<SessionExerciseId, Guid>(id => id.Value, v => SessionExerciseId.From(v));
public sealed class LoggedSetIdConverter() : ValueConverter<LoggedSetId, Guid>(id => id.Value, v => LoggedSetId.From(v));
public sealed class ExerciseIdConverter() : ValueConverter<ExerciseId, Guid>(id => id.Value, v => ExerciseId.From(v));

public static class TypedIdConventions
{
    /// <summary>Call from every module context's ConfigureConventions.</summary>
    public static void AddTypedIds(this ModelConfigurationBuilder builder)
    {
        builder.Properties<UserId>().HaveConversion<UserIdConverter>();
        builder.Properties<ProgramId>().HaveConversion<ProgramIdConverter>();
        builder.Properties<ProgramDayId>().HaveConversion<ProgramDayIdConverter>();
        builder.Properties<PrescriptionId>().HaveConversion<PrescriptionIdConverter>();
        builder.Properties<SessionId>().HaveConversion<SessionIdConverter>();
        builder.Properties<SessionExerciseId>().HaveConversion<SessionExerciseIdConverter>();
        builder.Properties<LoggedSetId>().HaveConversion<LoggedSetIdConverter>();
        builder.Properties<ExerciseId>().HaveConversion<ExerciseIdConverter>();
    }
}
