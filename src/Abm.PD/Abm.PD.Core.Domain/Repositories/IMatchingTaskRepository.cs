using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Repositories;

public interface IMatchingTaskRepository
{
    Task<IReadOnlyList<MatchingTask>> GetAllAsync(CancellationToken cancellationToken);

    Task<MatchingTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<MatchingTask> AddAsync(
        MatchingTask matchingTask,
        CancellationToken cancellationToken);

    Task<MatchingTask?> UpdateAsync(
        int id,
        MatchingTask matchingTask,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MatchingTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken);
}
