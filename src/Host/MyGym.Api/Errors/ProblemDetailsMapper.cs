using MyGym.Modules.ExerciseCatalog.Contracts;
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

        // RULING R25 (Task 12 review): a request body referencing a nonexistent exercise (e.g.
        // POST /api/programs with a bad exerciseId) is 422, not 404 — the endpoint and the
        // addressed program both exist; only the submitted content is semantically invalid, which
        // the client should group with the other field-validation failures below (rep_range.
        // inverted, warmup.percent_not_ascending, ...). Named explicitly rather than left to the
        // default so the choice is visible, and so a rename of the constant breaks this file at
        // compile time instead of silently falling out of sync. Contrast
        // TrainingErrors.SessionExerciseNotFound above, which addresses a session exercise by id
        // in the route itself and is correctly 404.
        ExerciseCatalogErrors.ExerciseNotFound => StatusCodes.Status422UnprocessableEntity,

        _ => StatusCodes.Status422UnprocessableEntity
    };
}
