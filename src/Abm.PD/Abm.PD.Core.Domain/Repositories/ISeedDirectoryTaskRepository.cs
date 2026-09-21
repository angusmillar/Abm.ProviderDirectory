using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Repositories;

public interface ISeedDirectoryTaskRepository
{
    Task<IReadOnlyList<SeedDirectoryTask>> GetAllAsync(CancellationToken cancellationToken);

    Task<SeedDirectoryTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<SeedDirectoryTask> AddAsync(
        SeedDirectoryTask seedDirectoryTask,
        CancellationToken cancellationToken);

    Task<SeedDirectoryTask?> UpdateAsync(
        int id,
        SeedDirectoryTask seedDirectoryTask,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SeedDirectoryTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken);
}
