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
    IFhirBulkExporter fhirBulkExporter)
{
    private Stopwatch? Stopwatch;

    public async Task Run(
        CancellationToken cancellationToken)
    {
        StartStopwatch();
        
        Parameters parameters = GetSmallExportParametersResource(
            fromDateTime: DateTimeSupport.GetDateTimeOffset("2026-08-23T00:00:00+10:00"), 
            toDateTime: DateTimeSupport.GetDateTimeOffset("2026-08-25T10:00:00+10:00"));

        FhirBulkExportState bulkExportState = await fhirBulkExporter.BeginExport(parameters, cancellationToken);

        logger.LogInformation("{@FhirBulkExportState}", bulkExportState);


        while (bulkExportState.SessionStatus == FhirBulkExportSessionStatus.InProgress)
        {
            await Task.Delay(20000, cancellationToken);
            bulkExportState = await fhirBulkExporter.DeleteExport(cancellationToken);
            // bulkExportState = await fhirBulkExporter.PollExport(cancellationToken);
            // ArgumentNullException.ThrowIfNull(bulkExportState.StartTime);
            // logger.LogInformation("{Status}: {Time} : {JobId}",
            //     bulkExportState.SessionStatus,
            //     DateTimeOffset.Now.Subtract(bulkExportState.StartTime.Value).ToString(),
            //     bulkExportState.JobId);
        }

        ArgumentNullException.ThrowIfNull(bulkExportState.Manifest);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        string json = JsonSerializer.Serialize(bulkExportState.Manifest, options);
        await File.WriteAllTextAsync(@"C:\Temp\Abm.ProviderDirectory\Manifest.json", json, cancellationToken);

        logger.LogInformation("{@Manifest}",
            bulkExportState.Manifest);

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
            Value = new Instant() { Value = fromDateTime}
        });
        parameters.Parameter.Add(new Parameters.ParameterComponent()
        {
            Name = "_until",
            Value = new Instant() { Value = toDateTime}
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