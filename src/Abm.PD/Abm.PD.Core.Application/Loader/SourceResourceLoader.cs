using Abm.Core.Time;
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.Core.Application.Settings;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.Loader;

/// <summary>
/// Persists the resources of a bulk export into the local source_resource store without an intermediate disk
/// write, by gathering them into fixed size batches and committing each as it fills - the local-store analogue
/// of Abm.PD.BulkExport.Loader.FhirBatchLoader.
///
/// The commit is pipelined one batch deep, the same way FhirBatchLoader's is: the previous commit is awaited
/// only once the next batch has filled, so the export's response stream keeps being read while the database
/// write is in flight. A stalled read is what lets a gateway decide the download connection has gone idle and
/// reset it.
/// </summary>
public class SourceResourceLoader(
    ILogger<SourceResourceLoader> logger,
    IOptions<SourceResourceLoaderSettings> settings,
    IDateTimeProvider dateTimeProvider,
    ISourceResourceRepository sourceResourceRepository) : ISourceResourceLoader
{
    public async Task<SourceResourceLoadResult> Load(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        Guid correlationId,
        DataSource dataSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exportResources);
        ArgumentNullException.ThrowIfNull(dataSource);

        int batchSize = settings.Value.BatchSize;
        LoadTally tally = new();
        List<SourceResource> batch = new(capacity: batchSize);
        Task? commitInFlight = null;

        try
        {
            await foreach (FhirBulkExportResource exportResource in
                           exportResources.WithCancellation(cancellationToken))
            {
                tally.SubmittedCount++;

                SourceResource? sourceResource = await TryConvertAsync(exportResource, correlationId, dataSource, tally);
                if (sourceResource is null)
                {
                    continue;
                }

                batch.Add(sourceResource);

                if (batch.Count < batchSize)
                {
                    continue;
                }

                //The previous commit is awaited here, once the next batch has filled, rather than straight after
                //it was started - see the class doc comment.
                await AwaitCommitInFlight();

                commitInFlight = CommitBatch(batch, tally, cancellationToken);

                //A new list rather than Clear(), because the previous one still belongs to the commit in flight.
                batch = new List<SourceResource>(capacity: batchSize);
            }

            await AwaitCommitInFlight();

            //An export will not divide evenly into batches, so the remainder is still to be committed.
            if (batch.Count > 0)
            {
                await CommitBatch(batch, tally, cancellationToken);
            }
        }
        finally
        {
            //A commit still running when the read loop failed has to be observed, or its exception surfaces
            //later as an unobserved task exception instead of against this load.
            if (commitInFlight is not null)
            {
                try
                {
                    await commitInFlight;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "The batch commit that was in flight when the load stopped also failed");
                }
            }
        }

        LogSummary(tally);

        return new SourceResourceLoadResult(
            SubmittedCount: tally.SubmittedCount,
            CommittedCount: tally.CommittedCount,
            FailedCount: tally.FailedCount,
            BatchCount: tally.BatchCount,
            RetainedFailures: tally.RetainedFailures);

        async Task AwaitCommitInFlight()
        {
            if (commitInFlight is null)
            {
                return;
            }

            Task commit = commitInFlight;
            commitInFlight = null;
            await commit;
        }
    }

    private async Task<SourceResource?> TryConvertAsync(
        FhirBulkExportResource exportResource,
        Guid correlationId,
        DataSource dataSource,
        LoadTally tally)
    {
        //The natural key (CorrelationId, ResourceType, ResourceId) can not be formed without an id, so the resource is
        //reported and skipped rather than thrown, so that the rest of the export still lands.
        if (string.IsNullOrWhiteSpace(exportResource.Resource.Id))
        {
            RecordFailure(
                tally,
                exportResource,
                ["The resource carries no id, so it can not be tracked in the source store."]);

            return null;
        }

        if (exportResource.Resource.Meta?.LastUpdated is not DateTimeOffset resourceLastUpdated)
        {
            RecordFailure(
                tally,
                exportResource,
                ["The resource carries no meta.lastUpdated, so it can not be tracked in the source store."]);

            return null;
        }

        DateTime nowUtc = dateTimeProvider.Now.UtcDateTime;

        return new SourceResource
        {
            CorrelationId = correlationId,
            ResourceType = exportResource.Resource.TypeName,
            ResourceId = exportResource.Resource.Id,
            ResourceLastUpdated = resourceLastUpdated,
            DataSourceId = dataSource.Id,
            DataSource = dataSource,
            Resource = await exportResource.Resource.ToJsonAsync(),
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
        };
    }

    private async Task CommitBatch(
        List<SourceResource> batch,
        LoadTally tally,
        CancellationToken cancellationToken)
    {
        int batchNumber = ++tally.BatchCount;

        logger.LogInformation(
            "Persisting batch {BatchNumber} of {EntryCount} resource(s) to source_resource",
            batchNumber,
            batch.Count);

        await sourceResourceRepository.AddRangeAsync(batch, cancellationToken);

        tally.CommittedCount += batch.Count;
    }

    private void RecordFailure(
        LoadTally tally,
        FhirBulkExportResource exportResource,
        string[] errorMessages)
    {
        tally.FailedCount++;

        logger.LogWarning(
            "{ResourceType}/{ResourceId} from line {LineNumber} of {SourceUrl} was not persisted: {ErrorMessages}",
            exportResource.Resource.TypeName,
            exportResource.Resource.Id ?? "[None]",
            exportResource.LineNumber,
            exportResource.SourceUrl,
            string.Join("; ", errorMessages));

        //Every failure is counted and logged, but only the first few are retained, so an export that fails on
        //every resource still holds bounded memory.
        if (tally.RetainedFailures.Count >= settings.Value.MaxRetainedFailures)
        {
            return;
        }

        tally.RetainedFailures.Add(new SourceResourceLoadFailure(
            ResourceType: exportResource.Resource.TypeName,
            ResourceId: exportResource.Resource.Id,
            SourceUrl: exportResource.SourceUrl,
            LineNumber: exportResource.LineNumber,
            ErrorMessages: errorMessages));
    }

    private void LogSummary(
        LoadTally tally)
    {
        logger.LogInformation(
            "Load complete: {CommittedCount} of {SubmittedCount} resource(s) persisted to source_resource over " +
            "{BatchCount} batch(es), {FailedCount} failed",
            tally.CommittedCount,
            tally.SubmittedCount,
            tally.BatchCount,
            tally.FailedCount);
    }

    /// <summary>
    /// The running counts of one load. Mutable, and deliberately not a record: it is handed to each commit so
    /// that the tally outlives the batch that produced it.
    /// </summary>
    private sealed class LoadTally
    {
        public long SubmittedCount;
        public long CommittedCount;
        public long FailedCount;
        public int BatchCount;
        public List<SourceResourceLoadFailure> RetainedFailures { get; } = [];
    }
}
