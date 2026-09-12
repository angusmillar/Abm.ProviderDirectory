using Abm.Core.HostedService;
using Abm.Core.Time;
using Abm.PD.Core.Application.Settings;
using Abm.PD.BulkExport.Loader;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Abm.PD.Core.Application;

public class ExportLoaderTaskScheduler(
    IExportLoaderTaskRepository repository,
    IExportRunner exportRunner,
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

        IReadOnlyList<ExportLoaderTask> due = await repository.FindDueAsync(nowUtc, cancellationToken);
        foreach (ExportLoaderTask task in due)
        {
            if (!await repository.TryClaimAsync(task.Id, nowUtc, cancellationToken))
            {
                // Another replica (or a human via the CRUD API) already claimed or changed this task
                // since FindDueAsync ran - this is the expected, silent outcome of losing the race.
                continue;
            }

            try
            {
                FhirBatchLoadResult result = await exportRunner.Run(task, cancellationToken);
                await repository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Completed,
                    dateTimeProvider.Now.UtcDateTime,
                    $"Committed {result.CommittedCount} of {result.SubmittedCount}, {result.FailedCount} failed",
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "ExportLoaderTask {TaskCode} failed", task.Code);
                await repository.RecordOutcomeAsync(
                    task.Id,
                    TaskStateId.Failed,
                    dateTimeProvider.Now.UtcDateTime,
                    exception.Message,
                    cancellationToken);
            }
        }
    }
}
