using System.Security.Claims;
using MyGym.SharedKernel;

namespace MyGym.Api.Auth;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    UserId UserId { get; }
}

/// <summary>
/// Reads both <see cref="ClaimTypes.NameIdentifier"/> and the raw "sub" claim. JWT bearer
/// authentication remaps inbound "sub" claims to <see cref="ClaimTypes.NameIdentifier"/> by default
/// (see the MapInboundClaims note in Program.cs), so the first lookup is the one that actually
/// matches at runtime; "sub" is kept as a fallback so this stays correct if that mapping is ever
/// disabled.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => TryGetUserId(out _);

    public UserId UserId => TryGetUserId(out var userId)
        ? userId
        : throw new InvalidOperationException("No authenticated user on this request.");

    private bool TryGetUserId(out UserId userId)
    {
        userId = default;
        var raw = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? accessor.HttpContext?.User.FindFirstValue("sub");

        if (!Guid.TryParse(raw, out var parsed))
            return false;

        userId = SharedKernel.UserId.From(parsed);
        return true;
    }
}
