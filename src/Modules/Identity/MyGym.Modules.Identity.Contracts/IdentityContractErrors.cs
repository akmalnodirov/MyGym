namespace MyGym.Modules.Identity.Contracts;

/// <summary>
/// Error codes that cross the identity module's boundary — part of the published contract, not an
/// implementation detail. Identity.Domain's <c>IdentityErrors.UserNotFound</c> points back at
/// <see cref="UserNotFound"/> rather than redeclaring the string, and Training.Application's
/// handlers read it directly (Training.Application may reference Identity.Contracts but not
/// Identity.Domain), so there is exactly one definition of this wire-contract code no matter which
/// side of the module boundary needs it. Same precedent as
/// <c>ExerciseCatalogErrors.ExerciseNotFound</c> (RULING R26), applied here because C3 requires
/// Training.Application to report "user not found" and the module boundary rules forbid it from
/// referencing Identity.Domain to get the constant.
/// </summary>
public static class IdentityContractErrors
{
    public const string UserNotFound = "identity.user_not_found";
}
