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

        // I8: malformed-input, not a validation-of-an-otherwise-sound-payload failure — the
        // Telegram initData itself could not be turned into a user at all, which is the same class
        // as AuthErrors.InitDataMalformed above, not the 422 default these two fell into before.
        IdentityErrors.TelegramIdInvalid => StatusCodes.Status400BadRequest,
        IdentityErrors.FirstNameRequired => StatusCodes.Status400BadRequest,

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
        TrainingErrors.ProgramEditConflict => StatusCodes.Status409Conflict,

        // I8: all three are raised by bodiless POSTs (/activate, /complete) — there is no payload
        // for the client to edit, so this mapper's own rule ("422 means the payload itself was
        // unacceptable") puts them with the other state conflicts above, not the 422 default they
        // fell into before. That previously left /activate returning 409 for program.already_active
        // and program.archived but 422 for program.needs_at_least_one_day — same class, same
        // endpoint, two codes.
        TrainingErrors.SessionNeedsWorkingSet => StatusCodes.Status409Conflict,
        TrainingErrors.ProgramNeedsDay => StatusCodes.Status409Conflict,
        TrainingErrors.ProgramDayNeedsPrescription => StatusCodes.Status409Conflict,

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
