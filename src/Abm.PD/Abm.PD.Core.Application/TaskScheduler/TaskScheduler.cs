using Abm.Core.HostedService;
using Abm.Core.Time;
using Abm.PD.Core.Application.ExportTaskRunner;
using Abm.PD.Core.Application.Loader;
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

            // MatchingTask is now TPH-mapped but has no runner wired up yet (MatchingTaskRunner.Run
            // throws NotImplementedException) - a claimed MatchingTask row falls through to here and
            // is marked Failed rather than being handed to a runner. ImportTask still has no mapped
            // CLR subtype at all, so a row carrying that TypeId would throw during EF materialisation
            // inside FindDueAsync instead, aborting the whole tick.
            if (task is not ExportTask)
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

            // Each task gets its own DI scope so its IExportRunner - and the scoped IFhirExporter/
            // IFhirBulkExporter underneath it - is a fresh instance. FhirBulkExporter is a stateful,
            // one-instance-one-export-session service; sharing one instance across every task in a tick
            // (as constructor injection into this class would do) made every task after the first fail
            // with "session already completed".
            using IServiceScope taskScope = serviceScopeFactory.CreateScope();
            IExportTaskRunner exportTaskRunner = taskScope.ServiceProvider.GetRequiredService<IExportTaskRunner>();

            try
            {
                // task (from ITaskRepository) never has its DataSource navigation loaded - it's
                // re-fetched here through IExportTaskRepository, which Includes it, rather than
                // passed straight to IExportRunner.Run.
                ExportTask exportTask = await exportTaskRepository.GetByIdAsync(task.Id, cancellationToken)
                    ?? throw new InvalidOperationException($"ExportTask {task.Id} was claimed but no longer exists");

                SourceResourceLoadResult result = await exportTaskRunner.Run(exportTask, correlationId, cancellationToken);
                await taskRepository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Completed,
                    dateTimeProvider.Now.UtcDateTime,
                    $"Persisted {result.CommittedCount} of {result.SubmittedCount}, {result.FailedCount} failed",
                    FailureCountUpdate.Reset,
                    correlationId,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "ExportTask {TaskCode} failed", task.Code);
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
}
