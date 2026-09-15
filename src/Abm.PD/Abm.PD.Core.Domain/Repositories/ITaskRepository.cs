using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Repositories;

public interface ITaskRepository
{
    Task<IReadOnlyList<TaskBase>> FindDueAsync(
        DateTime nowUtc,
        int failureAttemptCount,
        CancellationToken cancellationToken);

    Task<bool> TryClaimAsync(
        int id,
        Guid correlationId,
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
        Guid? correlationId,
        CancellationToken cancellationToken);
}
