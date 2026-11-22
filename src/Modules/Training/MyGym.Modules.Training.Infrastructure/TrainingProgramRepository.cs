using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

internal sealed class TrainingProgramRepository(TrainingDbContext context) : ITrainingProgramRepository
{
    public Task<TrainingProgram?> GetByIdAsync(ProgramId id, CancellationToken ct) =>
        context.Programs.SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task AddAsync(TrainingProgram program, CancellationToken ct) =>
        await context.Programs.AddAsync(program, ct);

    public void Remove(TrainingProgram program) => context.Programs.Remove(program);
}
