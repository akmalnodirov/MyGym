using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Contracts;

public sealed record AppUserRegistered(UserId UserId, long TelegramUserId, DateTimeOffset RegisteredAt)
    : IDomainEvent;
