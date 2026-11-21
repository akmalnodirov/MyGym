using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Programs;

public interface ITrainingProgramRepository
{
    Task<TrainingProgram?> GetByIdAsync(ProgramId id, CancellationToken ct);
    Task AddAsync(TrainingProgram program, CancellationToken ct);
    void Remove(TrainingProgram program);
}
