using Abm.PD.Domain.DateTimeSupport;
using Abm.PD.Domain.FhirBulkExport;
using Abm.PD.Domain.Models.Manifest;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Domain.Exporter;

public class FhirExporter(
    ILogger<FhirExporter> logger,
    IDateTimeProvider dateTimeProvider,
    IFhirBulkExporter fhirBulkExporter) : IFhirExporter
{
    private TimeSpan PollingTimeSpan = TimeSpan.FromSeconds(30);
    private FhirBulkExportState? BulkExportState;

    public async Task<FhirBulkExportManifest?> RequestDownloadManifest(
        Parameters parameters,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("== Submit Request ================================================================");
        logger.LogInformation("FHIR Bulk Data Export session started");
        BulkExportState = await fhirBulkExporter.BeginExport(parameters, cancellationToken);

        if (BulkExportState.SessionStatus == FhirBulkExportSessionStatus.InProgress)
        {
            ArgumentNullException.ThrowIfNull(BulkExportState.JobId);
            logger.LogInformation("JobId {JobId} Export request sent",
                BulkExportState.JobId);
            LogExportParameters(BulkExportState.JobId, parameters);
            logger.LogInformation("== Polling ====================================================================");
            logger.LogInformation(
                "JobId {JobId} The server accepted the request and is now building the download manifest",
                BulkExportState.JobId);
            logger.LogInformation(
                "JobId {JobId} Client polling, for build completion, is set for every {PollingSeconds} ",
                BulkExportState.JobId,
                PollingTimeSpan.ToNarrative());
            ArgumentNullException.ThrowIfNull(BulkExportState.StartTime);
            logger.LogInformation(
                "JobId {JobId} Server's Manifest build {Status} after {TimeSpan} with: {ProgressMessage}",
                BulkExportState.JobId,
                BulkExportState.SessionStatus,
                dateTimeProvider.Now.Subtract(BulkExportState.StartTime.Value).ToNarrative(),
                "FHIR Bulk Export Manifest build initiated");
        }

        if (BulkExportState.SessionStatus != FhirBulkExportSessionStatus.InProgress)
        {
            logger.LogInformation("{@BulkExportState}", BulkExportState);
            throw new ApplicationException("Failed to submit the FHIR Bulk Export request, see application logs");
        }

        while (BulkExportState.SessionStatus == FhirBulkExportSessionStatus.InProgress)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "JobId {JobId} Cancellation was requested by the client's callers",
                    BulkExportState.JobId);
                return null;
            }

            await Task.Delay(PollingTimeSpan, cancellationToken);
            //bulkExportState = await fhirBulkExporter.DeleteExport(cancellationToken);
            BulkExportState = await fhirBulkExporter.PollExport(cancellationToken);
            ArgumentNullException.ThrowIfNull(BulkExportState.StartTime);
            logger.LogInformation(
                "JobId {JobId} Server's Manifest build {Status} after {TimeSpan} with {ProgressMessage}",
                BulkExportState.JobId,
                BulkExportState.SessionStatus,
                dateTimeProvider.Now.Subtract(BulkExportState.StartTime.Value).ToNarrative(),
                BulkExportState.ProgressMessage ?? "[No Message]");
            if (BulkExportState.RequestedPollDelay is not null)
            {
                if (PollingTimeSpan != BulkExportState.RequestedPollDelay)
                {
                    PollingTimeSpan = BulkExportState.RequestedPollDelay.Value;
                    logger.LogInformation(
                        "JobId {JobId} The server requested that polling be every {RequestedPollDelay}",
                        BulkExportState.JobId,
                        BulkExportState.RequestedPollDelay.Value.ToNarrative());
                    logger.LogInformation("JobId {JobId} Client polling has been set to every {RequestedPollDelay}",
                        BulkExportState.JobId,
                        BulkExportState.RequestedPollDelay.Value.ToNarrative());
                }
            }
        }

        ArgumentNullException.ThrowIfNull(BulkExportState.Manifest);

        // string json =
        //     JsonSerializer.Serialize(bulkExportState.Manifest, new JsonSerializerOptions { WriteIndented = true });
        // await File.WriteAllTextAsync(@"C:\Temp\Abm.ProviderDirectory\Manifest.json", json, cancellationToken);

        LogManifest(BulkExportState);

        return BulkExportState.Manifest;
    }

    public IAsyncEnumerable<FhirBulkExportResource> StreamedExportFileList(
        CancellationToken cancellationToken) => fhirBulkExporter.GetExport(cancellationToken);
    
    private void LogExportParameters(
        string jobID,
        Parameters parameters)
    {
        logger.LogInformation("JobId {JobId} Input Parameters: ",
            jobID);

        foreach (Parameters.ParameterComponent parameterComponent in parameters.Parameter)
        {
            string value = "[Only valueString supported]";
            if (parameterComponent.Value is FhirString fhirString)
            {
                value = fhirString.Value;
            }

            if (parameterComponent.Value is Instant fhirInstant)
            {
                DateTimeOffset? time = fhirInstant.Value;
                if (time is not null)
                {
                    value = time.Value.ToString("u");
                }
            }

            logger.LogInformation("JobId {JobId}   parameter {Name}: {ValueString}",
                jobID,
                parameterComponent.Name,
                value);
        }
    }

    private void LogManifest(
        FhirBulkExportState bulkExportState)
    {
        ArgumentNullException.ThrowIfNull(bulkExportState.StartTime);
        ArgumentNullException.ThrowIfNull(bulkExportState.Manifest);
        logger.LogInformation("== Download Manifest Summary ===================================================");
        logger.LogInformation("JobId {JobId} Manifest received after: {TimeSpan}",
            bulkExportState.JobId,
            dateTimeProvider.Now.Subtract(bulkExportState.StartTime.Value).ToNarrative());

        logger.LogInformation("JobId {JobId} Manifest summary: ",
            bulkExportState.JobId);

        logger.LogInformation(
            "JobId {JobId}   Manifest Transaction Time {TransactionTime}",
            bulkExportState.JobId,
            bulkExportState.Manifest.TransactionTime.ToString("s"));

        logger.LogInformation(
            "JobId {JobId}   Manifest Output {OutputCount}",
            bulkExportState.JobId,
            bulkExportState.Manifest.Output?.Count ?? 0);

        logger.LogInformation(
            "JobId {JobId}   Manifest Deleted {Deleted}",
            bulkExportState.JobId,
            bulkExportState.Manifest.Deleted?.Count ?? 0);

        logger.LogInformation(
            "JobId {JobId}   Manifest Outcome {TransactionTime}",
            bulkExportState.JobId,
            bulkExportState.Manifest.Outcome?.Count ?? 0);

        logger.LogInformation(
            "JobId {JobId}   Manifest Error {TransactionTime}",
            bulkExportState.JobId,
            bulkExportState.Manifest.Error?.Count ?? 0);
    }
}