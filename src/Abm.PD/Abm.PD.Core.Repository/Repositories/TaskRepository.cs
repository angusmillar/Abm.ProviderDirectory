using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository.Repositories;

public class TaskRepository(ProviderDirectoryDbContext dbContext) : ITaskRepository
{
    public async Task<IReadOnlyList<TaskBase>> FindDueAsync(
        DateTime nowUtc,
        int failureAttemptCount,
        CancellationToken cancellationToken)
    {
        return await dbContext.Tasks
            .Where(t => t.State == TaskStateId.Ready
                        || t.State == TaskStateId.Completed
                        || (t.State == TaskStateId.Failed && t.FailureCount <= failureAttemptCount))
            .Where(t => t.TriggerEvery > TimeSpan.Zero)
            .Where(t => t.StartAtUtc == null || t.StartAtUtc <= nowUtc)
            .Where(t => t.EndAtUtc == null || t.EndAtUtc >= nowUtc)
            .Where(t => t.LastStartUtc == null || t.LastStartUtc + t.TriggerEvery <= nowUtc)
            .Where(t => t.MaxRunCount == null || t.RunCount < t.MaxRunCount)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryClaimAsync(
        int id,
        Guid correlationId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        int rows = await dbContext.Tasks
            .Where(t => t.Id == id
                        && (t.State == TaskStateId.Ready
                            || t.State == TaskStateId.Completed
                            || t.State == TaskStateId.Failed))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, TaskStateId.InProgress)
                .SetProperty(t => t.LastStartUtc, nowUtc)
                .SetProperty(t => t.StateReason, (string?)null)
                .SetProperty(t => t.LastCorrelationId, correlationId), cancellationToken);
        return rows == 1;
    }

    public async Task ReapStaleInProgressAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken)
    {
        await dbContext.Tasks
            .Where(t => t.State == TaskStateId.InProgress && t.LastStartUtc != null && t.LastStartUtc < olderThanUtc)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, TaskStateId.Failed)
                .SetProperty(t => t.StateReason, "Reaped: exceeded expected run duration")
                .SetProperty(t => t.FailureCount, t => t.FailureCount + 1), cancellationToken);
    }

    public async Task RecordOutcomeAsync(
        int id,
        TaskStateId state,
        DateTime nowUtc,
        string? stateReason,
        FailureCountUpdate failureCountUpdate,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        // ExecuteUpdateAsync takes an expression tree, so the FailureCount branch can't be factored
        // out into a shared block-bodied lambda - each outcome gets its own single, atomic UPDATE.
        // RunCount is tied directly to state == Completed rather than to failureCountUpdate, so it
        // stays correct even if a future caller records Completed without also resetting FailureCount.
        switch (failureCountUpdate)
        {
            case FailureCountUpdate.Reset:
                await dbContext.Tasks
                    .Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.State, state)
                        .SetProperty(t => t.LastEndUtc, nowUtc)
                        .SetProperty(t => t.StateReason, stateReason)
                        .SetProperty(t => t.LastCorrelationId, correlationId)
                        .SetProperty(t => t.FailureCount, 0)
                        .SetProperty(t => t.RunCount, t => state == TaskStateId.Completed ? t.RunCount + 1 : t.RunCount),
                        cancellationToken);
                break;
            case FailureCountUpdate.Increment:
                await dbContext.Tasks
                    .Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.State, state)
                        .SetProperty(t => t.LastEndUtc, nowUtc)
                        .SetProperty(t => t.StateReason, stateReason)
                        .SetProperty(t => t.LastCorrelationId, correlationId)
                        .SetProperty(t => t.FailureCount, t => t.FailureCount + 1)
                        .SetProperty(t => t.RunCount, t => state == TaskStateId.Completed ? t.RunCount + 1 : t.RunCount),
                        cancellationToken);
                break;
            default:
                await dbContext.Tasks
                    .Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.State, state)
                        .SetProperty(t => t.LastEndUtc, nowUtc)
                        .SetProperty(t => t.StateReason, stateReason)
                        .SetProperty(t => t.LastCorrelationId, correlationId)
                        .SetProperty(t => t.RunCount, t => state == TaskStateId.Completed ? t.RunCount + 1 : t.RunCount),
                        cancellationToken);
                break;
        }
    }
}
