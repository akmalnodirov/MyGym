using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Contracts;

public sealed record ProgramActivated(ProgramId ProgramId, UserId OwnerUserId, int RevisionNumber)
    : IDomainEvent;

public sealed record WorkoutSessionStarted(SessionId SessionId, UserId UserId, DateTimeOffset StartedAt)
    : IDomainEvent;

public sealed record WorkoutSessionCompleted(
    SessionId SessionId,
    UserId UserId,
    DateTimeOffset CompletedAt,
    int WorkingSetCount) : IDomainEvent;
