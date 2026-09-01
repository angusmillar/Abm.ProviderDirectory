using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Abm.PD.Console.Settings;
using Abm.PD.Domain.DateTimeSupport;
using Abm.PD.Domain.Exporter;
using Abm.PD.Domain.FhirBulkExport;
using Abm.PD.Domain.HttpClientSupport;
using Abm.PD.Domain.Models.Manifest;
using FhirNavigator;
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
    IFhirExporter fhirExporter,
    IFhirNavigatorFactory fhirNavigatorFactory)
{
    private Stopwatch? Stopwatch;
    private TimeSpan PollingTimeSpan = TimeSpan.FromSeconds(30);
    private DirectoryInfo OutputDirectoryInfo = new(@"C:\Temp\Abm.ProviderDirectory\Output");
    

    public async Task Run(
        CancellationToken cancellationToken)
    {
        StartStopwatch();

        logger.LogInformation("== Begin Request ================================================================");
        logger.LogInformation("FHIR Bulk Data Export session started");
        
        //Parameters parameters = GetExportParametersResource();
        Parameters parameters =
            GetSmallExportParametersResource(
                fromDateTime: DateTimeSupport.GetDateTimeOffset("2026-08-23T00:00:00+10:00"));
        //Parameters parameters = GetPractitionerLargeExportParametersResource(fromDateTime: DateTimeSupport.GetDateTimeOffset("2020-01-01T00:00:00+10:00"));

        FhirBulkExportManifest? fhirBulkExportManifest =
            await fhirExporter.RequestDownloadManifest(parameters, cancellationToken);

        ArgumentNullException.ThrowIfNull(fhirBulkExportManifest);

        PrepareOutputDirectory();

        IFhirNavigator fhirNavigator = fhirNavigatorFactory.GetFhirNavigator(HttpClientType.TargetProviderDirectoryServer);
        
        //GetExport streams the NDJSON output files, so this loop never holds more than one resource at a time.
        int resourceCount = 0;
        await foreach (FhirBulkExportResource exportResource in fhirExporter.StreamedExportFileList(cancellationToken))
        {
            resourceCount++;
            await ProcessResource(resourceCount, exportResource, fhirNavigator, cancellationToken);
        }

        logger.LogInformation("Downloaded {ResourceCount} resource(s) from the export",
            resourceCount);

        logger.LogInformation("== Session Ended Completed =====================================================");
        
        EndStopwatch();
    }

    private async Task ProcessResource(
        int resourceCount,
        FhirBulkExportResource fhirBulkExportResource,
        IFhirNavigator fhirNavigator,
        CancellationToken cancellationToken)
    {
        LogResource(fhirBulkExportResource);

        
        
        
        await File.WriteAllTextAsync(Path.Combine(
                OutputDirectoryInfo.FullName, 
                $"{fhirBulkExportResource.Resource.TypeName}-{fhirBulkExportResource.Resource.Id}.json"),
            await fhirBulkExportResource.Resource.ToJsonAsync(), cancellationToken);
        
        
    }

    private void LogResource(
        FhirBulkExportResource resource)
    {
        logger.LogInformation("{ResourceType}/{ResourceId} read from line {LineNumber} of {SourceUrl}",
            resource.Resource.TypeName,
            resource.Resource.Id,
            resource.LineNumber,
            resource.SourceUrl);
    }

    private void PrepareOutputDirectory()
    {
        if (!OutputDirectoryInfo.Exists)
        {
            OutputDirectoryInfo.Create();
        }

        foreach (FileInfo fileInfo in OutputDirectoryInfo.GetFiles("*.json"))
        {
            fileInfo.Delete();
        }
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