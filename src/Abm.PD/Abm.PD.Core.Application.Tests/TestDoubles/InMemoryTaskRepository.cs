using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// Hand rolled, in-memory fake for TaskScheduler tests that need a real (if simplistic)
// FindDueAsync/TryClaimAsync/RecordOutcomeAsync/ReapStaleInProgressAsync - no claim atomicity is
// modelled, this is single-threaded test code driving the scheduler directly.
public sealed class InMemoryTaskRepository(List<TaskBase> tasks) : ITaskRepository
{
    public Task<IReadOnlyList<TaskBase>> FindDueAsync(
        DateTime nowUtc,
        int failureAttemptCount,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TaskBase> due = tasks
            .Where(t => t.State == TaskStateId.Ready
                        || t.State == TaskStateId.Completed
                        || (t.State == TaskStateId.Failed && t.FailureCount <= failureAttemptCount))
            .Where(t => t.TriggerEvery > TimeSpan.Zero)
            .Where(t => t.StartAtUtc == null || t.StartAtUtc <= nowUtc)
            .Where(t => t.EndAtUtc == null || t.EndAtUtc >= nowUtc)
            .Where(t => t.LastStartUtc == null || t.LastStartUtc + t.TriggerEvery <= nowUtc)
            .Where(t => t.MaxRunCount == null || t.RunCount < t.MaxRunCount)
            .ToList();
        return Task.FromResult(due);
    }

    public Task<bool> TryClaimAsync(
        int id,
        Guid correlationId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        TaskBase? task = tasks.SingleOrDefault(t => t.Id == id);
        if (task is null || task.State == TaskStateId.InProgress)
        {
            return Task.FromResult(false);
        }

        task.State = TaskStateId.InProgress;
        task.LastStartUtc = nowUtc;
        task.StateReason = null;
        task.LastCorrelationId = correlationId;
        return Task.FromResult(true);
    }

    public Task ReapStaleInProgressAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken)
    {
        foreach (TaskBase task in tasks.Where(
                     t => t.State == TaskStateId.InProgress && t.LastStartUtc != null && t.LastStartUtc < olderThanUtc))
        {
            task.State = TaskStateId.Failed;
            task.StateReason = "Reaped: exceeded expected run duration";
            task.FailureCount++;
        }

        return Task.CompletedTask;
    }

    public Task RecordOutcomeAsync(
        int id,
        TaskStateId state,
        DateTime nowUtc,
        string? stateReason,
        FailureCountUpdate failureCountUpdate,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        TaskBase? task = tasks.SingleOrDefault(t => t.Id == id);
        if (task is not null)
        {
            task.State = state;
            task.LastEndUtc = nowUtc;
            task.StateReason = stateReason;
            task.LastCorrelationId = correlationId;
            task.FailureCount = failureCountUpdate switch
            {
                FailureCountUpdate.Reset => 0,
                FailureCountUpdate.Increment => task.FailureCount + 1,
                _ => task.FailureCount
            };
            if (state == TaskStateId.Completed)
            {
                task.RunCount++;
            }
        }

        return Task.CompletedTask;
    }
}
