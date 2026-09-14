using Abm.Core.HostedService;
using Abm.Core.Time;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Application.Settings;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Abm.PD.Core.Application;

public class ExportLoaderTaskScheduler(
    IExportLoaderTaskRepository repository,
    IServiceScopeFactory serviceScopeFactory,
    IDateTimeProvider dateTimeProvider,
    IOptions<ExportLoaderTaskSchedulerSettings> settings,
    ILogger<ExportLoaderTaskScheduler> logger) : ITimedHostedService
{
    public async Task DoWork(
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = dateTimeProvider.Now.UtcDateTime;

        await repository.ReapStaleInProgressAsync(
            nowUtc - settings.Value.StaleInProgressAfter, cancellationToken);

        IReadOnlyList<ExportLoaderTask> dueExportLoaderTaskList = await repository.FindDueAsync(
            nowUtc, settings.Value.FailureAttemptCount, cancellationToken);
        if (dueExportLoaderTaskList.Count == 0)
        {
            logger.LogInformation("{Service} for {Instance} found no tasks due to run", 
                nameof(ITimedHostedService), 
                nameof(ExportLoaderTaskScheduler));    
        }
        
        foreach (ExportLoaderTask task in dueExportLoaderTaskList)
        {
            if (!await repository.TryClaimAsync(task.Id, nowUtc, cancellationToken))
            {
                // Another replica (or a human via the CRUD API) already claimed or changed this task
                // since FindDueAsync ran - this is the expected, silent outcome of losing the race.
                continue;
            }

            // Each task gets its own DI scope so its IExportRunner - and the scoped IFhirExporter/
            // IFhirBulkExporter underneath it - is a fresh instance. FhirBulkExporter is a stateful,
            // one-instance-one-export-session service; sharing one instance across every task in a tick
            // (as constructor injection into this class would do) made every task after the first fail
            // with "session already completed".
            using IServiceScope taskScope = serviceScopeFactory.CreateScope();
            IExportRunner exportRunner = taskScope.ServiceProvider.GetRequiredService<IExportRunner>();

            try
            {
                SourceResourceLoadResult result = await exportRunner.Run(task, cancellationToken);
                await repository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Completed,
                    dateTimeProvider.Now.UtcDateTime,
                    $"Persisted {result.CommittedCount} of {result.SubmittedCount}, {result.FailedCount} failed",
                    FailureCountUpdate.Reset,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "ExportLoaderTask {TaskCode} failed", task.Code);
                await repository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Failed,
                    dateTimeProvider.Now.UtcDateTime,
                    exception.Message,
                    FailureCountUpdate.Increment,
                    CancellationToken.None);
            }
        }
    }
}
