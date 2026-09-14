using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// Hand rolled, in-memory fake for ExportLoaderTaskScheduler tests that need a real (if simplistic)
// FindDueAsync/TryClaimAsync/RecordOutcomeAsync/ReapStaleInProgressAsync - no claim atomicity is
// modelled, this is single-threaded test code driving the scheduler directly.
public sealed class InMemoryExportLoaderTaskRepository(List<ExportLoaderTask> tasks) : IExportLoaderTaskRepository
{
    public Task<IReadOnlyList<ExportLoaderTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ExportLoaderTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ExportLoaderTask> AddAsync(
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ExportLoaderTask?> UpdateAsync(
        int id,
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<ExportLoaderTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<ExportLoaderTask>> FindDueAsync(
        DateTime nowUtc,
        int failureAttemptCount,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExportLoaderTask> due = tasks
            .Where(t => t.State == TaskStateId.Ready
                        || t.State == TaskStateId.Completed
                        || (t.State == TaskStateId.Failed && t.FailureCount <= failureAttemptCount))
            .Where(t => t.TriggerEvery > TimeSpan.Zero)
            .Where(t => t.ToStartAtUtc == null || t.ToStartAtUtc <= nowUtc)
            .Where(t => t.ToEndAtUtc == null || t.ToEndAtUtc >= nowUtc)
            .Where(t => t.LastStart == null || t.LastStart + t.TriggerEvery <= nowUtc)
            .ToList();
        return Task.FromResult(due);
    }

    public Task<bool> TryClaimAsync(
        int id,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ExportLoaderTask? task = tasks.SingleOrDefault(t => t.Id == id);
        if (task is null || task.State == TaskStateId.InProgress)
        {
            return Task.FromResult(false);
        }

        task.State = TaskStateId.InProgress;
        task.LastStart = nowUtc;
        task.StateReason = null;
        return Task.FromResult(true);
    }

    public Task ReapStaleInProgressAsync(
        DateTime olderThanUtc,
        CancellationToken cancellationToken)
    {
        foreach (ExportLoaderTask task in tasks.Where(
                     t => t.State == TaskStateId.InProgress && t.LastStart != null && t.LastStart < olderThanUtc))
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
        CancellationToken cancellationToken)
    {
        ExportLoaderTask? task = tasks.SingleOrDefault(t => t.Id == id);
        if (task is not null)
        {
            task.State = state;
            task.LastEnd = nowUtc;
            task.StateReason = stateReason;
            task.FailureCount = failureCountUpdate switch
            {
                FailureCountUpdate.Reset => 0,
                FailureCountUpdate.Increment => task.FailureCount + 1,
                _ => task.FailureCount
            };
        }

        return Task.CompletedTask;
    }
}
