using System.Diagnostics;
using System.Globalization;
using Abm.PD.BulkExport;
using Abm.PD.Console.Settings;
using Abm.PD.BulkExport.Loader;
using Abm.PD.BulkExport.Models;
using Abm.PD.BulkExport.Writer;
using Abm.PD.Core.Application;
using Abm.PD.Core.Application.ExportTaskRunner;
using Abm.PD.Core.Application.Identifers;
using Abm.PD.Core.Application.Settings;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Console;

// The x6 Resource for Provider Directory
// Practitioner
// Endpoint
// Organization
//  Location
//  HealthcareService
//  PractitionerRole
//Provenance

public class ConsoleApplication(
    ILogger<ConsoleApplication> logger,
    IOptions<ConsoleApplicationSettings> appSettings,
    IFhirExporter fhirExporter,
    IFhirDiskWriter fhirDiskWriter)
{

    private const string FhirNavigatorRepositoryCode = "ProviderConnectAustralia";
    private Stopwatch? Stopwatch;
    private TimeSpan PollingTimeSpan = TimeSpan.FromSeconds(30);

    public async Task Run(
        CancellationToken cancellationToken)
    {
        StartStopwatch();


        var task = FhirTaskFactory.CreateFhirTask();

        string json = await task.ToJsonAsync();
        
        logger.LogInformation("== Begin Request ================================================================");
        logger.LogInformation("FHIR Bulk Data Export session started");
        
        Parameters parameters = FhirExportQuery.GetByPostCode();
        
        // Parameters parameters = FhirExportQuery.GetSmallExportParametersResource(
        //     fromDateTime: DateTimeSupport.GetDateTimeOffset("2026-08-23T00:00:00+10:00"));
        
        // Parameters parameters = FhirExportQuery.GetPractitionerLargeExportParametersResource(
        //     fromDateTime: DateTimeSupport.GetDateTimeOffset("2020-01-01T00:00:00+10:00"));

        FhirBulkExportManifest? fhirBulkExportManifest =
            await fhirExporter.RequestDownloadManifest(parameters, FhirNavigatorRepositoryCode, cancellationToken);

        ArgumentNullException.ThrowIfNull(fhirBulkExportManifest);
        
        //Load exported resource into a target FHIR Server
        //The export stream is handed straight to the loader: it batches the resources as they arrive and never
        //holds more than two batches, so the resources go from the download to the target server without ever
        //being collected in full or written to disk.
        //logger.LogInformation("== Load ========================================================================");
        // FhirBatchLoadResult loadResult = await fhirBatchLoader.Load(
        //     exportResources: fhirExporter.StreamedExportFileList(cancellationToken),
        //     cancellationToken: cancellationToken);
        // LogFhirBatchLoadResult(loadResult);
        
        //Write exported resource to a directory on disk
        logger.LogInformation("== Output to: {Directory} ======================================================="
            , fhirDiskWriter.OutputDirectoryInfo);
        await fhirDiskWriter.Write(exportResources: fhirExporter.StreamedExportFileList(cancellationToken),
            cancellationToken: cancellationToken);
        
        logger.LogInformation("== Session Ended Completed =====================================================");
        
        EndStopwatch();
    }

    private void LogFhirBatchLoadResult(
        FhirBatchLoadResult loadResult)
    {
        logger.LogInformation(
            "Committed {CommittedCount} of {SubmittedCount} resource(s) over {BatchCount} batch(es), " +
            "{FailedCount} failed",
            loadResult.CommittedCount,
            loadResult.SubmittedCount,
            loadResult.BatchCount,
            loadResult.FailedCount);

        //Only the first few failures are retained, so say when the list is not the whole story.
        if (loadResult.FailedCount > loadResult.RetainedFailures.Count)
        {
            logger.LogWarning(
                "Only the first {RetainedFailureCount} of {FailedCount} failure(s) are listed here, the rest are " +
                "in the log above",
                loadResult.RetainedFailures.Count,
                loadResult.FailedCount);
        }

        foreach (FhirBatchLoadFailure failure in loadResult.RetainedFailures)
        {
            logger.LogWarning(
                "Failed {ResourceType}/{ResourceId} from line {LineNumber} of {SourceUrl}: {ErrorMessages}",
                failure.ResourceType,
                failure.ResourceId ?? "[None]",
                failure.LineNumber,
                failure.SourceUrl,
                string.Join("; ", failure.ErrorMessages));
        }
    }
    
    private void EndStopwatch()
    {
        ArgumentNullException.ThrowIfNull(Stopwatch);
        Stopwatch.Stop();
        logger.LogInformation("{ApplicationName} completed in {Elapsed} ms", appSettings.Value.ApplicationName,
            Stopwatch.ElapsedMilliseconds);
    }

    private Stopwatch StartStopwatch()
    {
        Stopwatch = Stopwatch.StartNew();
        logger.LogInformation("{ApplicationName} started", appSettings.Value.ApplicationName);
        return Stopwatch;
    }
}