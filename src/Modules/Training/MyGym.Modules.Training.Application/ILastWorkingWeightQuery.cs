using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application;

public interface ILastWorkingWeightQuery
{
    /// <summary>
    /// The heaviest working set logged for each exercise in the athlete's most recent completed
    /// session that contained it. Exercises with no history are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<ExerciseId, Weight>> GetTopWorkingWeightsAsync(
        UserId userId, IReadOnlyCollection<ExerciseId> exerciseIds, CancellationToken ct);
}
