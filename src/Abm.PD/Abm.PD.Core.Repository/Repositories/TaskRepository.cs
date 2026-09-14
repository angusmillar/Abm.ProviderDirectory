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
            .Where(t => t.ToStartAtUtc == null || t.ToStartAtUtc <= nowUtc)
            .Where(t => t.ToEndAtUtc == null || t.ToEndAtUtc >= nowUtc)
            .Where(t => t.LastStart == null || t.LastStart + t.TriggerEvery <= nowUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryClaimAsync(
        int id,
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
                .SetProperty(t => t.LastStart, nowUtc)
                .SetProperty(t => t.StateReason, (string?)null), cancellationToken);
        return rows == 1;
    }

    public async Task ReapStaleInProgressAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken)
    {
        await dbContext.Tasks
            .Where(t => t.State == TaskStateId.InProgress && t.LastStart != null && t.LastStart < olderThanUtc)
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
        CancellationToken cancellationToken)
    {
        // ExecuteUpdateAsync takes an expression tree, so the FailureCount branch can't be factored
        // out into a shared block-bodied lambda - each outcome gets its own single, atomic UPDATE.
        switch (failureCountUpdate)
        {
            case FailureCountUpdate.Reset:
                await dbContext.Tasks
                    .Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.State, state)
                        .SetProperty(t => t.LastEnd, nowUtc)
                        .SetProperty(t => t.StateReason, stateReason)
                        .SetProperty(t => t.FailureCount, 0), cancellationToken);
                break;
            case FailureCountUpdate.Increment:
                await dbContext.Tasks
                    .Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.State, state)
                        .SetProperty(t => t.LastEnd, nowUtc)
                        .SetProperty(t => t.StateReason, stateReason)
                        .SetProperty(t => t.FailureCount, t => t.FailureCount + 1), cancellationToken);
                break;
            default:
                await dbContext.Tasks
                    .Where(t => t.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.State, state)
                        .SetProperty(t => t.LastEnd, nowUtc)
                        .SetProperty(t => t.StateReason, stateReason), cancellationToken);
                break;
        }
    }
}
