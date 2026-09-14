using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Repositories;

public interface IExportLoaderTaskRepository
{
    Task<IReadOnlyList<ExportLoaderTask>> GetAllAsync(CancellationToken cancellationToken);

    Task<ExportLoaderTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<ExportLoaderTask> AddAsync(
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken);

    Task<ExportLoaderTask?> UpdateAsync(
        int id,
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExportLoaderTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExportLoaderTask>> FindDueAsync(
        DateTime nowUtc,
        int failureAttemptCount,
        CancellationToken cancellationToken);

    Task<bool> TryClaimAsync(
        int id,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task ReapStaleInProgressAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken);

    Task RecordOutcomeAsync(
        int id,
        TaskStateId state,
        DateTime nowUtc,
        string? stateReason,
        FailureCountUpdate failureCountUpdate,
        CancellationToken cancellationToken);
}
