using Abm.PD.BulkExport.Exceptions;
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.FhirSupport;
using Abm.PD.BulkExport.Settings;
using FhirNavigator.FhirHttpClient;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.BulkExport.Loader;

/// <summary>
/// Moves the resources of a bulk export into the target provider directory without an intermediate disk write,
/// by gathering them into fixed size FHIR batch Bundles of PUT entries and committing each bundle as it fills.
///
/// A <b>batch</b> rather than a transaction: batch entries are processed independently, so a resource the server
/// refuses fails on its own instead of losing the whole bundle, and there is no single long-running server side
/// transaction over hundreds of writes.
///
/// <b>PUT</b> rather than POST: the resources already carry the ids the directory published them under, and an
/// update is idempotent, so a retried commit, or a load restarted after a failed download, replays without
/// duplicating anything. The target server creates a stub for a reference it has not seen yet and fills that
/// stub in when the real resource arrives later in the load, so no dependency ordering is needed here.
/// </summary>
public class FhirBatchLoader(
    ILogger<FhirBatchLoader> logger,
    IOptions<FhirBatchLoaderSettings> settings,
    IFhirHttpClientFactory fhirHttpClientFactory) : IFhirBatchLoader
{
    /// <summary>
    /// Reads the export stream to its end, committing a batch each time
    /// <see cref="FhirBatchLoaderSettings.BatchSize"/> resources have been gathered.
    ///
    /// A resource the target server refuses is recorded and the load carries on, because one bad resource is a
    /// data problem rather than a reason to abandon the export. A failure of the commit itself is systemic — the
    /// retry policy has already given up by that point — so it is thrown and the load stops.
    /// </summary>
    public async Task<FhirBatchLoadResult> Load(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        string repositoryCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exportResources);

        FhirClient fhirClient = fhirHttpClientFactory.CreateClient(repositoryCode);

        int batchSize = settings.Value.BatchSize;
        LoadTally tally = new();
        List<FhirBulkExportResource> batch = new(capacity: batchSize);
        Task? commitInFlight = null;

        try
        {
            await foreach (FhirBulkExportResource exportResource in
                           exportResources.WithCancellation(cancellationToken))
            {
                tally.SubmittedCount++;

                if (!TryAddToBatch(batch, exportResource, tally))
                {
                    continue;
                }

                if (batch.Count < batchSize)
                {
                    continue;
                }

                //The previous commit is awaited here, once the next batch has filled, rather than straight after
                //it was started. That keeps the export's response stream being read while the target server
                //works, and a stalled read is what lets a load balancer decide the download connection has gone
                //idle and reset it.
                await AwaitCommitInFlight();

                commitInFlight = CommitBatch(fhirClient, batch, tally, repositoryCode, cancellationToken);

                //A new list rather than Clear(), because the previous one still belongs to the commit in flight.
                batch = new List<FhirBulkExportResource>(capacity: batchSize);
            }

            await AwaitCommitInFlight();

            //An export will not divide evenly into batches, so the remainder is still to be committed.
            if (batch.Count > 0)
            {
                await CommitBatch(fhirClient, batch, tally, repositoryCode, cancellationToken);
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

        LogSummary(tally, repositoryCode);

        return new FhirBatchLoadResult(
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

    private bool TryAddToBatch(
        List<FhirBulkExportResource> batch,
        FhirBulkExportResource exportResource,
        LoadTally tally)
    {
        //A PUT addresses the resource by its id, so a resource without one can not be loaded at all. It is
        //reported and skipped rather than thrown, so that the rest of the export still lands.
        if (string.IsNullOrWhiteSpace(exportResource.Resource.Id))
        {
            RecordFailure(
                tally,
                exportResource,
                httpStatusCode: null,
                errorMessages: ["The resource carries no id, so it can not be loaded with a PUT."]);

            return false;
        }

        batch.Add(exportResource);
        return true;
    }

    private async Task CommitBatch(
        FhirClient fhirClient,
        List<FhirBulkExportResource> batch,
        LoadTally tally,
        string repositoryCode,
        CancellationToken cancellationToken)
    {
        int batchNumber = ++tally.BatchCount;

        Bundle requestBundle = new()
        {
            Type = Bundle.BundleType.Transaction,
            Entry = batch.Select(ToPutEntry).ToList()
        };

        logger.LogInformation(
            "Committing batch {BatchNumber} of {EntryCount} resource(s) to {RepositoryCode}",
            batchNumber,
            batch.Count,
            repositoryCode);

        Bundle? responseBundle = await fhirClient.TransactionAsync(requestBundle, cancellationToken);

        if (responseBundle is null)
        {
            throw new FhirBulkLoadException(
                $"The target server returned no Bundle in response to batch {batchNumber}.");
        }

        //The specification requires a batch-response to carry one entry for each request entry, in the same
        //order, which is the only thing tying an outcome back to the resource that produced it.
        if (responseBundle.Entry.Count != batch.Count)
        {
            throw new FhirBulkLoadException(
                $"The target server answered batch {batchNumber}'s {batch.Count} entries with " +
                $"{responseBundle.Entry.Count} response entries, so no outcome can be matched to its resource.");
        }

        for (int index = 0; index < batch.Count; index++)
        {
            RecordEntryOutcome(tally, batch[index], responseBundle.Entry[index]);
        }
    }

    private void RecordEntryOutcome(
        LoadTally tally,
        FhirBulkExportResource exportResource,
        Bundle.EntryComponent responseEntry)
    {
        int? httpStatusCode = ParseStatusCode(responseEntry.Response?.Status);

        //A batch answers 200 OK once it has processed the bundle, whatever became of the individual entries, so
        //an entry's own status is the only report that its resource was refused.
        if (httpStatusCode is >= 200 and <= 299)
        {
            tally.CommittedCount++;
            return;
        }

        string[] errorMessages = responseEntry.Response?.Outcome is OperationOutcome operationOutcome
            ? OperationOutcomeSupport.ExtractErrorMessages(operationOutcome)
            : [$"The target server answered the entry with the status {responseEntry.Response?.Status ?? "[None]"}."];

        RecordFailure(tally, exportResource, httpStatusCode, errorMessages);
    }

    private void RecordFailure(
        LoadTally tally,
        FhirBulkExportResource exportResource,
        int? httpStatusCode,
        string[] errorMessages)
    {
        tally.FailedCount++;

        logger.LogWarning(
            "{ResourceType}/{ResourceId} from line {LineNumber} of {SourceUrl} was not loaded, HTTP status " +
            "{HttpStatusCode}: {ErrorMessages}",
            exportResource.Resource.TypeName,
            exportResource.Resource.Id ?? "[None]",
            exportResource.LineNumber,
            exportResource.SourceUrl,
            httpStatusCode?.ToString() ?? "[None]",
            string.Join("; ", errorMessages));

        //Every failure is counted and logged, but only the first few are retained, so an export that fails on
        //every resource still holds bounded memory.
        if (tally.RetainedFailures.Count >= settings.Value.MaxRetainedFailures)
        {
            return;
        }

        tally.RetainedFailures.Add(new FhirBatchLoadFailure(
            ResourceType: exportResource.Resource.TypeName,
            ResourceId: exportResource.Resource.Id,
            SourceUrl: exportResource.SourceUrl,
            LineNumber: exportResource.LineNumber,
            HttpStatusCode: httpStatusCode,
            ErrorMessages: errorMessages));
    }

    private static Bundle.EntryComponent ToPutEntry(
        FhirBulkExportResource exportResource)
    {
        return new Bundle.EntryComponent
        {
            Resource = exportResource.Resource,

            //No FullUrl: a batch resolves nothing between its own entries, and every entry addresses its
            //resource by type and id in the request url, so all a fullUrl could add here is the source server's
            //address on a resource being written to a different server.
            FullUrl = $"{exportResource.Resource.TypeName}/{exportResource.Resource.Id}",
            Request = new Bundle.RequestComponent
            {
                Method = Bundle.HTTPVerb.PUT,

                //No IfMatch: the update has to overwrite whatever the target holds, including a stub the server
                //created earlier for a reference that had not been loaded yet. A version aware update would be
                //refused with a 409 against that stub.
                Url = $"{exportResource.Resource.TypeName}/{exportResource.Resource.Id}"
            }
        };
    }

    private static int? ParseStatusCode(
        string? status)
    {
        //A batch-response status reads "200 OK" or a bare "201", so only the leading code can be relied on.
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        string code = status.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return int.TryParse(code, out int httpStatusCode) ? httpStatusCode : null;
    }

    private void LogSummary(
        LoadTally tally,
        string repositoryCode)
    {
        logger.LogInformation(
            "Load complete: {CommittedCount} of {SubmittedCount} resource(s) committed to {RepositoryCode} over " +
            "{BatchCount} batch(es), {FailedCount} failed",
            tally.CommittedCount,
            tally.SubmittedCount,
            repositoryCode,
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
        public List<FhirBatchLoadFailure> RetainedFailures { get; } = [];
    }
}
