using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
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
// Practitioner
// Endpoint
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
    private TimeSpan PollingTimeSpan = TimeSpan.FromSeconds(30);

    public async Task Run(
        CancellationToken cancellationToken)
    {
        StartStopwatch();

        logger.LogInformation("== Begin Request ================================================================");
        logger.LogInformation("FHIR Bulk Data Export session started"); 
        
        
        Parameters parameters = GetExportParametersResource();
        //Parameters parameters = GetSmallExportParametersResource(fromDateTime: DateTimeSupport.GetDateTimeOffset("2026-08-23T00:00:00+10:00"));
        //Parameters parameters = GetPractitionerLargeExportParametersResource(fromDateTime: DateTimeSupport.GetDateTimeOffset("2020-01-01T00:00:00+10:00"));
        
        FhirBulkExportState bulkExportState = await fhirBulkExporter.BeginExport(parameters, cancellationToken);

        
        if (bulkExportState.SessionStatus == FhirBulkExportSessionStatus.InProgress)
        {
            
            ArgumentNullException.ThrowIfNull(bulkExportState.JobId);
            logger.LogInformation("JobId {JobId} Export request sent",
                bulkExportState.JobId); 
            LogExportParameters(bulkExportState.JobId, parameters);
            logger.LogInformation("== Polling ====================================================================");
            logger.LogInformation("JobId {JobId} The server accepted the request and is now building the download manifest", 
                bulkExportState.JobId);
            logger.LogInformation("JobId {JobId} Client polling, for build completion, is set for every {PollingSeconds} ", 
                bulkExportState.JobId,
                PollingTimeSpan.ToNarrative());
            ArgumentNullException.ThrowIfNull(bulkExportState.StartTime);
            logger.LogInformation("JobId {JobId} Server's Manifest build {Status} after {TimeSpan} with: {ProgressMessage}",
                bulkExportState.JobId,
                bulkExportState.SessionStatus,
                dateTimeProvider.Now.Subtract(bulkExportState.StartTime.Value).ToNarrative(),
                "FHIR Bulk Export Manifest build initiated");
        }

        if (bulkExportState.SessionStatus != FhirBulkExportSessionStatus.InProgress)
        {
            logger.LogInformation("{@BulkExportState}", bulkExportState);
            throw new ApplicationException("Failed to submit the FHIR Bulk Export request, see application logs");
        }

        while (bulkExportState.SessionStatus == FhirBulkExportSessionStatus.InProgress)
        {
            await Task.Delay(PollingTimeSpan, cancellationToken);
            //bulkExportState = await fhirBulkExporter.DeleteExport(cancellationToken);
            bulkExportState = await fhirBulkExporter.PollExport(cancellationToken);
            ArgumentNullException.ThrowIfNull(bulkExportState.StartTime);
            logger.LogInformation("JobId {JobId} Server's Manifest build {Status} after {TimeSpan} with {ProgressMessage}",
                bulkExportState.JobId,
                bulkExportState.SessionStatus,
                dateTimeProvider.Now.Subtract(bulkExportState.StartTime.Value).ToNarrative(),
                bulkExportState.ProgressMessage ?? "[No Message]");
            if (bulkExportState.RequestedPollDelay is not null)
            {
                if (PollingTimeSpan != bulkExportState.RequestedPollDelay)
                {
                    PollingTimeSpan = bulkExportState.RequestedPollDelay.Value;
                    logger.LogInformation("JobId {JobId} The server requested that polling be every {RequestedPollDelay}",
                        bulkExportState.JobId,
                        bulkExportState.RequestedPollDelay.Value.ToNarrative());
                    logger.LogInformation("JobId {JobId} Client polling has been set to every {RequestedPollDelay}",
                        bulkExportState.JobId,
                        bulkExportState.RequestedPollDelay.Value.ToNarrative());
                }
            }
        }

        ArgumentNullException.ThrowIfNull(bulkExportState.Manifest);

        string json =
            JsonSerializer.Serialize(bulkExportState.Manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(@"C:\Temp\Abm.ProviderDirectory\Manifest.json", json, cancellationToken);
        
        LogManifest(bulkExportState);

        logger.LogInformation("== Downloading =================================================================");
        logger.LogInformation("JobId {JobId} Begin File Downloading",
            bulkExportState.JobId);
        
        //GetExport streams the NDJSON output files, so this loop never holds more than one resource at a time.
        int resourceCount = 0;
        await foreach (FhirBulkExportResource exportResource in fhirBulkExporter.GetExport(cancellationToken))
        {
            resourceCount++;
            logger.LogInformation("JobId {JobId} {ResourceType}/{ResourceId} read from line {LineNumber} of {SourceUrl}",
                bulkExportState.JobId,
                exportResource.Resource.TypeName,
                exportResource.Resource.Id,
                exportResource.LineNumber,
                exportResource.SourceUrl);

            await File.WriteAllTextAsync(
                @$"C:\Temp\Abm.ProviderDirectory\Output\{exportResource.Resource.TypeName}-{exportResource.Resource.Id}.json",
                await exportResource.Resource.ToJsonAsync(), cancellationToken);
        }

        logger.LogInformation("JobId {JobId} Downloaded {ResourceCount} resource(s) from the export", 
            bulkExportState.JobId, 
            resourceCount);

        logger.LogInformation("== Session Ended Completed =====================================================");
        
        EndStopwatch();
    }

    private void LogExportParameters(string jobID,
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
                DateTimeOffset? time  = fhirInstant.Value;
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
        DateTimeOffset fromDateTime)
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

    // Practitioner
// Endpoint
// Organization
//  Location
//  HealthcareService
//  PractitionerRole
    private static Parameters GetPractitionerLargeExportParametersResource(
        DateTimeOffset fromDateTime)
    {
        var since = new Instant() { Value = fromDateTime };
        
        Parameters parameters = new Parameters();
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_outputFormat",
            Value = new FhirString("application/fhir+ndjson")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_since",
            Value = since
        });
        
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_type",
            Value = new FhirString("Organization,Location,Endpoint,Practitioner,HealthcareService,PractitionerRole")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString($"Organization?_lastUpdated=ge{since}")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString($"Location?_lastUpdated=ge{since}")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString($"Endpoint?_lastUpdated=ge{since}")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString($"Practitioner?_lastUpdated=ge{since}")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString($"HealthcareService?_lastUpdated=ge{since}")
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_typeFilter",
            Value = new FhirString($"PractitionerRole?_lastUpdated=ge{since}")
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