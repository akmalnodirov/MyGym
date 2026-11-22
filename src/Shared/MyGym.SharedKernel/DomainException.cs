namespace MyGym.SharedKernel;

/// <summary>
/// Spec §9's second error mechanism, alongside <see cref="Result{T}"/>. A <see cref="Result{T}"/>
/// models an expected business failure — a second concurrent session, a reversed rep range —
/// returned to the caller as a normal outcome. A <see cref="DomainException"/> signals a
/// structurally invalid state that should be unreachable through any valid application code path —
/// for example a persisted row that fails the exact validation the domain enforced before it was
/// ever written. These are bugs, not business outcomes, and must surface loudly (unhandled, not
/// quietly mapped to a 4xx) so they show up in telemetry instead of being swallowed.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
