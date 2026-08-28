using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Abm.PD.Console.Settings;
using Abm.PD.Domain.DateTimeSupport;
using Abm.PD.Domain.FhirBulkExport;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Console;

// Load order based on resource references
// Endpoint
// Practitioner
// Organization
//  Location
//  HealthcareService
//  PractitionerRole

public class ConsoleApplication(
    ILogger<ConsoleApplication> logger,
    IOptions<ConsoleApplicationSettings> appSettings,
    IDateTimeProvider dateTimeProvider,
    IFhirBulkExporter fhirBulkExporter)
{
    private Stopwatch? Stopwatch;
    private const int PollingSeconds = 20;

    public async Task Run(
        CancellationToken cancellationToken)
    {
        StartStopwatch();

        Parameters parameters = GetSmallExportParametersResource(
            fromDateTime: DateTimeSupport.GetDateTimeOffset("2026-08-23T00:00:00+10:00"),
            toDateTime: DateTimeSupport.GetDateTimeOffset("2026-08-25T10:00:00+10:00"));

        FhirBulkExportState bulkExportState = await fhirBulkExporter.BeginExport(parameters, cancellationToken);

        if (bulkExportState.SessionStatus == FhirBulkExportSessionStatus.InProgress)
        {
            logger.LogInformation("Polling for export preparation status every {PollingSeconds} seconds", PollingSeconds);
            ArgumentNullException.ThrowIfNull(bulkExportState.StartTime);
            logger.LogInformation("JobId: {JobId} status is {Status} after {TimeSpan}",
                bulkExportState.JobId,
                bulkExportState.SessionStatus,
                dateTimeProvider.Now.Subtract(bulkExportState.StartTime.Value).ToNarrative());
        }

        if (bulkExportState.SessionStatus != FhirBulkExportSessionStatus.InProgress)
        {
            logger.LogInformation("{@BulkExportState}", bulkExportState);
            throw new ApplicationException("Failed to BeginExport, see application logs");
        }

        while (bulkExportState.SessionStatus == FhirBulkExportSessionStatus.InProgress)
        {
            await Task.Delay((PollingSeconds * 1000), cancellationToken);
            //bulkExportState = await fhirBulkExporter.DeleteExport(cancellationToken);
            bulkExportState = await fhirBulkExporter.PollExport(cancellationToken);
            ArgumentNullException.ThrowIfNull(bulkExportState.StartTime);
            logger.LogInformation("JobId: {JobId} status is {Status} after {TimeSpan}",
                bulkExportState.JobId,
                bulkExportState.SessionStatus,
                dateTimeProvider.Now.Subtract(bulkExportState.StartTime.Value).ToNarrative());
        }

        ArgumentNullException.ThrowIfNull(bulkExportState.Manifest);

        string json =
            JsonSerializer.Serialize(bulkExportState.Manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(@"C:\Temp\Abm.ProviderDirectory\Manifest.json", json, cancellationToken);

        LogManifest(bulkExportState);

        //GetExport streams the NDJSON output files, so this loop never holds more than one resource at a time.
        int resourceCount = 0;
        await foreach (FhirBulkExportResource exportResource in fhirBulkExporter.GetExport(cancellationToken))
        {
            resourceCount++;
            logger.LogInformation("{ResourceType}/{ResourceId} read from line {LineNumber} of {SourceUrl}",
                exportResource.Resource.TypeName,
                exportResource.Resource.Id,
                exportResource.LineNumber,
                exportResource.SourceUrl);

            await File.WriteAllTextAsync(
                @$"C:\Temp\Abm.ProviderDirectory\{exportResource.Resource.TypeName}-{exportResource.Resource.Id}.json",
                await exportResource.Resource.ToJsonAsync(), cancellationToken);
        }

        logger.LogInformation("Read {ResourceCount} resource(s) from the export", resourceCount);

        EndStopwatch();
    }

    private void LogManifest(
        FhirBulkExportState bulkExportState)
    {
        ArgumentNullException.ThrowIfNull(bulkExportState.StartTime);
        ArgumentNullException.ThrowIfNull(bulkExportState.Manifest);
        
        logger.LogInformation("Data Export Manifest received for JobId {JobId} after: {TimeSpan}",
            bulkExportState.JobId,
            dateTimeProvider.Now.Subtract(bulkExportState.StartTime.Value).ToNarrative());
        
        logger.LogInformation(
            "JobId {JobId} Manifest Transaction {TransactionTime}",
            bulkExportState.JobId,
            bulkExportState.Manifest.TransactionTime.ToString("s"));
        
        logger.LogInformation(
            "JobId {JobId} Manifest Output {OutputCount}",
            bulkExportState.JobId,
            bulkExportState.Manifest.Output?.Count ?? 0);
        
        logger.LogInformation(
            "JobId {JobId} Manifest Deleted {Deleted}",
            bulkExportState.JobId,
            bulkExportState.Manifest.Deleted?.Count ?? 0);
        
        logger.LogInformation(
            "JobId {JobId} Manifest Outcome {TransactionTime}",
            bulkExportState.JobId,
            bulkExportState.Manifest.Outcome?.Count ?? 0);
        
        logger.LogInformation(
            "JobId {JobId} Manifest Error {TransactionTime}",
            bulkExportState.JobId,
            bulkExportState.Manifest.Error?.Count ?? 0);
        
    }

    private static Parameters GetExportParametersResource()
    {
        Parameters parameters = new Parameters();
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_outputFormat",
            Value = new FhirString("application/fhir+ndjson")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_type",
            Value = new FhirString("Location,HealthcareService,Organization,PractitionerRole,Practitioner")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString("Location?address-city=Balmain&address-postalcode=2041&near=-33.8607|151.1803|100")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString(
                "HealthcareService?service-type=http://snomed.info/sct|789718008&location.address-city=Balmain&location.address-postalcode=2041")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString(
                "Organization?_has:HealthcareService:organization:service-type=http://snomed.info/sct|789718008")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString(
                "PractitionerRole?location.address-city=Balmain&location.address-postalcode=2041")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString("Practitioner?_has:PractitionerRole:practitioner:location.address-city=Balmain")
        });

        return parameters;
    }

    private static Parameters GetSmallExportParametersResource(
        DateTimeOffset fromDateTime,
        DateTimeOffset toDateTime)
    {
        Parameters parameters = new Parameters();
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_outputFormat",
            Value = new FhirString("application/fhir+ndjson")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_since",
            Value = new Instant() { Value = fromDateTime }
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_until",
            Value = new Instant() { Value = toDateTime }
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_type",
            Value = new FhirString("Endpoint")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString("Endpoint?_lastUpdated=gt2010")
        });

        return parameters;
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