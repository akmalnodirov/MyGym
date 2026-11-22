using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.Modules.Identity.Domain;
using MyGym.Modules.Training.Domain;
using MyGym.Shared.Infrastructure.Auth;

namespace MyGym.Api.Errors;

public static class ProblemDetailsMapper
{
    /// <summary>
    /// Maps a stable domain error code to an HTTP status. Conflicts are state problems the client
    /// cannot fix by editing its payload; 422 means the payload itself was unacceptable.
    /// </summary>
    public static int StatusFor(string errorCode) => errorCode switch
    {
        AuthErrors.InitDataMalformed => StatusCodes.Status400BadRequest,
        AuthErrors.InitDataSignatureInvalid => StatusCodes.Status401Unauthorized,
        AuthErrors.InitDataExpired => StatusCodes.Status401Unauthorized,
        AuthErrors.RefreshTokenInvalid => StatusCodes.Status401Unauthorized,

        IdentityErrors.UserNotFound => StatusCodes.Status404NotFound,
        CatalogErrors.ExerciseNotFound => StatusCodes.Status404NotFound,
        TrainingErrors.ProgramNotFound => StatusCodes.Status404NotFound,
        TrainingErrors.ProgramDayNotFound => StatusCodes.Status404NotFound,
        TrainingErrors.SessionNotFound => StatusCodes.Status404NotFound,
        TrainingErrors.SessionExerciseNotFound => StatusCodes.Status404NotFound,
        TrainingErrors.SetNotFound => StatusCodes.Status404NotFound,

        TrainingErrors.SessionAlreadyInProgress => StatusCodes.Status409Conflict,
        TrainingErrors.SessionClosed => StatusCodes.Status409Conflict,
        TrainingErrors.ProgramAlreadyActive => StatusCodes.Status409Conflict,
        TrainingErrors.ProgramArchived => StatusCodes.Status409Conflict,
        TrainingErrors.ProgramNotEditable => StatusCodes.Status409Conflict,

        _ => StatusCodes.Status422UnprocessableEntity
    };
}
