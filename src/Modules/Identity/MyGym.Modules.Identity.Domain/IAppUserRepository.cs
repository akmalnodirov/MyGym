using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Domain;

public interface IAppUserRepository
{
    Task<AppUser?> GetByIdAsync(UserId id, CancellationToken ct);
    Task<AppUser?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken ct);
    Task AddAsync(AppUser user, CancellationToken ct);
    void Remove(AppUser user);
}
