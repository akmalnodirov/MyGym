using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Infrastructure;

internal sealed class AppUserRepository(IdentityDbContext context) : IAppUserRepository
{
    public Task<AppUser?> GetByIdAsync(UserId id, CancellationToken ct) =>
        context.Users.SingleOrDefaultAsync(u => u.Id == id, ct);

    public Task<AppUser?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken ct) =>
        context.Users.SingleOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);

    public async Task AddAsync(AppUser user, CancellationToken ct) => await context.Users.AddAsync(user, ct);

    public void Remove(AppUser user) => context.Users.Remove(user);
}
