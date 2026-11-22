using MyGym.Modules.Identity.Contracts;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application;

/// <summary>
/// C3: a null profile lookup means the user row no longer exists — every authenticated principal
/// was registered, so null here can only mean <c>DELETE /api/me</c> raced the request. The five
/// call sites below used to fall back to <c>new LoadingPreferences(WeightUnit.Kilograms, 2.5m)</c>
/// on a null lookup, which silently let a write succeed for a user whose account had already been
/// purged — inserting training rows keyed to a <c>user_id</c> no cross-schema FK will ever clean
/// up. Treating null as failure turns that into <see cref="IdentityContractErrors.UserNotFound"/>
/// (the mapper already sends it to 404), the same response an ordinary lookup-by-id miss produces.
/// RULING R31: narrow fix now; a token-version/denylist check is recorded as follow-up.
/// </summary>
internal static class LoadingPreferencesLookup
{
    public static async Task<Result<LoadingPreferences>> ResolveAsync(
        IAthleteProfileQueries profiles, UserId actor, CancellationToken ct)
    {
        var preferences = await profiles.GetLoadingPreferencesAsync(actor, ct);
        return preferences is null
            ? Result<LoadingPreferences>.Failure(new Error(IdentityContractErrors.UserNotFound, "User not found."))
            : Result<LoadingPreferences>.Success(preferences);
    }
}
