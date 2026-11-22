namespace MyGym.Modules.Training.Domain;

/// <summary>
/// I2: thrown by Infrastructure in place of letting a raw <c>DbUpdateException</c> or
/// <c>DbUpdateConcurrencyException</c> reach the host as an unmapped 500, for a write that
/// collided — under exactly the concurrency the corresponding database guard exists to handle —
/// with a unique index or concurrency token an application-level pre-check already tried and
/// failed to prevent. One type per guard; each write handler catches the one its own write can
/// trigger and maps it to the <see cref="Result"/> the guard itself describes. Declared in Domain
/// (not Infrastructure) so Application can catch them without taking a dependency on EF Core or
/// Npgsql.
/// </summary>
public sealed class SessionAlreadyInProgressConflictException : Exception;

/// <summary>
/// See <see cref="SessionAlreadyInProgressConflictException"/>. Raised when two concurrent retries
/// of the same (SessionExerciseId, ClientSetId) pair both pass <c>WorkoutSession.LogSet</c>'s
/// in-aggregate idempotency check and then race to insert. The sequential case is already
/// idempotent (the second request finds the first request's set and returns it as a success); this
/// exception exists so the <em>concurrent</em> case gets the same treatment instead of surfacing
/// the unique-index violation as an error.
/// </summary>
public sealed class DuplicateClientSetIdConflictException : Exception;

/// <summary>
/// See <see cref="SessionAlreadyInProgressConflictException"/>. Raised when
/// <c>TrainingProgram.RevisionNumber</c>'s optimistic concurrency token detects the row changed
/// since it was loaded — i.e. two concurrent edits to the same program.
/// </summary>
public sealed class ProgramEditConflictException : Exception;
