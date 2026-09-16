using Abm.Core.HostedService;
using Abm.Core.Time;
using Abm.PD.Core.Application.ExportTaskRunner;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Application.MatchingTaskRunner;
using Abm.PD.Core.Application.Settings;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Abm.PD.Core.Application.TaskScheduler;

public class TaskScheduler(
    ITaskRepository taskRepository,
    IExportTaskRepository exportTaskRepository,
    IMatchingTaskRepository matchingTaskRepository,
    IServiceScopeFactory serviceScopeFactory,
    IDateTimeProvider dateTimeProvider,
    IOptions<TaskSchedulerSettings> settings,
    ILogger<TaskScheduler> logger) : ITimedHostedService
{
    public async Task DoWork(
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = dateTimeProvider.Now.UtcDateTime;

        await taskRepository.ReapStaleInProgressAsync(
            nowUtc - settings.Value.StaleInProgressAfter, cancellationToken);

        IReadOnlyList<TaskBase> dueTaskList = await taskRepository.FindDueAsync(
            nowUtc, settings.Value.FailureAttemptCount, cancellationToken);
        if (dueTaskList.Count == 0)
        {
            logger.LogInformation("{Service} for {Instance} found no tasks due to run",
                nameof(ITimedHostedService),
                nameof(TaskScheduler));
        }

        foreach (TaskBase task in dueTaskList)
        {
            // Our own identifier for this run, independent of the FHIR bulk export server's JobId -
            // see the doc comment on TaskBase.LastCorrelationId. Minted here so TryClaimAsync can
            // persist it onto the task as part of the claim itself, rather than a later write.
            Guid correlationId = Guid.CreateVersion7();
            if (!await taskRepository.TryClaimAsync(task.Id, correlationId, nowUtc, cancellationToken))
            {
                // Another replica (or a human via the CRUD API) already claimed or changed this task
                // since FindDueAsync ran - this is the expected, silent outcome of losing the race.
                continue;
            }

            // ImportTask still has no mapped CLR subtype at all, so a row carrying that TypeId would
            // throw during EF materialisation inside FindDueAsync instead, aborting the whole tick.
            if (task is not ExportTask and not MatchingTask)
            {
                logger.LogWarning(
                    "Task {TaskCode} has unsupported {TypeId}, marking Failed", task.Code, task.TypeId);
                await taskRepository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Failed,
                    dateTimeProvider.Now.UtcDateTime,
                    $"Unsupported task type {task.TypeId}",
                    FailureCountUpdate.Increment,
                    correlationId,
                    CancellationToken.None);
                continue;
            }

            // Each task gets its own DI scope so its IExportTaskRunner/IMatchingTaskRunner - and the
            // scoped IFhirExporter/IFhirBulkExporter underneath the export one - is a fresh instance.
            // FhirBulkExporter is a stateful, one-instance-one-export-session service; sharing one
            // instance across every task in a tick (as constructor injection into this class would do)
            // made every task after the first fail with "session already completed".
            using IServiceScope taskScope = serviceScopeFactory.CreateScope();

            try
            {
                string outcomeReason = task switch
                {
                    ExportTask => await RunExportTask(taskScope, task.Id, correlationId, cancellationToken),
                    MatchingTask => await RunMatchingTask(taskScope, task.Id, correlationId, cancellationToken),
                    _ => throw new InvalidOperationException($"Task {task.Id} matched neither ExportTask nor MatchingTask despite the guard above"),
                };

                await taskRepository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Completed,
                    dateTimeProvider.Now.UtcDateTime,
                    outcomeReason,
                    FailureCountUpdate.Reset,
                    correlationId,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "{TaskTypeId} {TaskCode} failed", task.TypeId, task.Code);
                await taskRepository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Failed,
                    dateTimeProvider.Now.UtcDateTime,
                    exception.Message,
                    FailureCountUpdate.Increment,
                    correlationId,
                    CancellationToken.None);
            }
        }
    }

    private async Task<string> RunExportTask(
        IServiceScope taskScope,
        int taskId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        // task (from ITaskRepository) never has its DataSource navigation loaded - it's re-fetched
        // here through IExportTaskRepository, which Includes it, rather than passed straight to
        // IExportTaskRunner.Run.
        ExportTask exportTask = await exportTaskRepository.GetByIdAsync(taskId, cancellationToken)
            ?? throw new InvalidOperationException($"ExportTask {taskId} was claimed but no longer exists");

        IExportTaskRunner exportTaskRunner = taskScope.ServiceProvider.GetRequiredService<IExportTaskRunner>();
        SourceResourceLoadResult result = await exportTaskRunner.Run(exportTask, correlationId, cancellationToken);
        return $"Persisted {result.CommittedCount} of {result.SubmittedCount}, {result.FailedCount} failed";
    }

    private async Task<string> RunMatchingTask(
        IServiceScope taskScope,
        int taskId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        MatchingTask matchingTask = await matchingTaskRepository.GetByIdAsync(taskId, cancellationToken)
            ?? throw new InvalidOperationException($"MatchingTask {taskId} was claimed but no longer exists");

        IMatchingTaskRunner matchingTaskRunner = taskScope.ServiceProvider.GetRequiredService<IMatchingTaskRunner>();
        await matchingTaskRunner.Run(matchingTask, correlationId, cancellationToken);
        return "Matching task completed";
    }
}
