using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Abm.PD.Domain.HttpClientSupport;
using FhirNavigator.FhirHttpClient;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using System.Web;
using Abm.PD.Domain.DateTimeSupport;
using Abm.PD.Domain.Exceptions;
using Abm.PD.Domain.FhirSupport;
using Abm.PD.Domain.Models.Manifest;
using Abm.PD.Domain.NdJsonSupport;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Domain.FhirBulkExport;

public class FhirBulkExporter(
    ILogger<FhirBulkExporter> logger,
    IDateTimeProvider dateTimeProvider,
    IFhirHttpClientFactory fhirHttpClientFactory,
    IHttpClientFactory httpClientFactory) : IFhirBulkExporter
{
    private const string ExportOperationName = "export";
    private const string ExportPollStatusOperationName = "export-poll-status";
    private const string JobIdParameterName = "_jobId";
    private const string NdJsonMediaType = "application/fhir+ndjson";

    private static readonly JsonSerializerOptions ManifestJsonSerializerOptions = new(JsonSerializerDefaults.Web);

    //The FHIR POCOs need Firely's converters, the System.Text.Json defaults can not read them.
    private static readonly JsonSerializerOptions FhirNdJsonSerializerOptions =
        new JsonSerializerOptions().ForFhir(typeof(ModelInfo).Assembly);

    private string? JobId;
    private FhirBulkExportSessionStatus CurrentSessionStatus = FhirBulkExportSessionStatus.NotStarted;
    private DateTimeOffset? StartTime;
    private DateTimeOffset? EndTime;
    private OperationOutcome? OperationOutcome;
    private string[]? ErrorMessages;
    private FhirBulkExportManifest? Manifest;
    
    
    public async Task<FhirBulkExportState> BeginExport(
        Parameters parameters,
        CancellationToken cancellationToken)
    {
        FhirBulkExportSessionStatus[] allowedToStart = [FhirBulkExportSessionStatus.NotStarted, FhirBulkExportSessionStatus.Error];
        if (!allowedToStart.Contains(CurrentSessionStatus))
        {
            throw new FhirBulkExportException(
                $"Can not begin a new export while the current session is in the status: {CurrentSessionStatus}");
        }

        StartTime = dateTimeProvider.Now;
        EndTime = null;
        JobId = null;
        Manifest = null;

        FhirClient fhirClient = fhirHttpClientFactory.CreateClient(HttpClientType.ProviderConnectAustralia);
        SetOperationRequiredHeaders(fhirClient);

        try
        {
            await fhirClient.WholeSystemOperationAsync(
                operationName: ExportOperationName,
                parameters: parameters,
                useGet: false,
                ct: cancellationToken);
        }
        catch (FhirOperationException fhirOperationException)
        {
            return GetFhirOperationExceptionResponse(fhirOperationException);
        }
        
        string? httpStatus = fhirClient.LastResult?.Status;
        if (!IsAcceptedStatus(httpStatus))
        {
            return await GetSessionInErrorStatus(fhirClient, httpStatus);
        }

        JobId = GetJobId(fhirClient.LastResult?.Location);
        OperationOutcome = null;
        ErrorMessages = null;
        CurrentSessionStatus = FhirBulkExportSessionStatus.InProgress;
        return GetFhirBulkExportState();
    }
    
    public async Task<FhirBulkExportState> PollExport(
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(JobId);
        
        HttpClient httpClient = httpClientFactory.CreateClient(HttpClientType.ProviderConnectAustralia);
        
        // Relative, no leading slash — see the BaseAddress note below.
        Uri requestUri = new(GetBaseAddress(httpClient), $"${ExportPollStatusOperationName}?{JobIdParameterName}={Uri.EscapeDataString(JobId)}");   

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();

        //The Output Manifest is only returned once the export completes with a 200 OK, a 202 Accepted means the
        //job is still running and carries no HTTP Body.
        if (response.StatusCode is HttpStatusCode.Accepted)
        {
            CurrentSessionStatus = FhirBulkExportSessionStatus.InProgress;
            return GetFhirBulkExportState();
        }

        string json =  await response.Content.ReadAsStringAsync(cancellationToken);

        Manifest = DeserializeManifest(json);
        EndTime = dateTimeProvider.Now;
        OperationOutcome = null;
        ErrorMessages = null;
        CurrentSessionStatus = FhirBulkExportSessionStatus.Completed;

        return GetFhirBulkExportState();
    }

    
    public async IAsyncEnumerable<FhirBulkExportResource> GetExport(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (CurrentSessionStatus is not FhirBulkExportSessionStatus.Completed)
        {
            throw new FhirBulkExportException(
                $"Can not read the export output files while the current session is in the status: {CurrentSessionStatus}");
        }

        ArgumentNullException.ThrowIfNull(Manifest);

        if (!IsNdJsonOutputFormat(Manifest.OutputFormat))
        {
            throw new FhirBulkExportException(
                $"The Output Manifest declared the outputFormat {Manifest.OutputFormat}, only NDJSON output can be read.");
        }

        LogSkippedManifestFiles(Manifest);

        if (Manifest.Output is null || Manifest.Output.Count == 0)
        {
            logger.LogInformation("The Output Manifest listed no output files, the export produced no resources");
            yield break;
        }

        HttpClient httpClient = httpClientFactory.CreateClient(HttpClientType.ProviderConnectAustralia);

        //Files referenced by the continuesInFile of an entry are themselves entries in the output array, so
        //iterating the array reads every file exactly once.
        foreach (FhirBulkExportManifestOutputFile manifestOutputFile in Manifest.Output)
        {
            Uri outputFileUrl = new(manifestOutputFile.Url);

            logger.LogInformation(
                "Reading the {ResourceType} output file {OutputFileUrl}",
                manifestOutputFile.Type ?? "[Unknown]",
                outputFileUrl);

            await foreach (FhirBulkExportResource exportResource in ReadOutputFile(
                               httpClient,
                               manifestOutputFile,
                               outputFileUrl,
                               cancellationToken))
            {
                yield return exportResource;
            }
        }
    }

    private static async IAsyncEnumerable<FhirBulkExportResource> ReadOutputFile(
        HttpClient httpClient,
        FhirBulkExportManifestOutputFile manifestOutputFile,
        Uri outputFileUrl,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, outputFileUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(NdJsonMediaType));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        //ResponseHeadersRead: without it SendAsync buffers the entire output file into memory before returning.
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using Stream ndJsonStream = GetDecompressedStream(response, contentStream);

        await foreach (NdJsonLine<Resource> ndJsonLine in NdJsonReader.ReadAsync(
                           stream: ndJsonStream,
                           deserializer: DeserializeResource,
                           cancellationToken: cancellationToken))
        {
            yield return new FhirBulkExportResource(
                Resource: ndJsonLine.Value,
                ManifestOutputType: manifestOutputFile.Type,
                SourceUrl: outputFileUrl,
                LineNumber: ndJsonLine.LineNumber);
        }
    }

    private static Resource? DeserializeResource(
        string json)
    {
        return JsonSerializer.Deserialize<Resource>(json, FhirNdJsonSerializerOptions);
    }

    private static Stream GetDecompressedStream(
        HttpResponseMessage response,
        Stream contentStream)
    {
        //An HttpClient handler configured for automatic decompression strips Content-Encoding, so anything still
        //named here is compression this method must undo itself.
        string? contentEncoding = response.Content.Headers.ContentEncoding.LastOrDefault();

        return contentEncoding?.ToLowerInvariant() switch
        {
            "gzip" => new GZipStream(contentStream, CompressionMode.Decompress),
            "deflate" => new DeflateStream(contentStream, CompressionMode.Decompress),
            "br" => new BrotliStream(contentStream, CompressionMode.Decompress),
            _ => contentStream
        };
    }

    private static bool IsNdJsonOutputFormat(
        string? outputFormat)
    {
        //The specification defaults outputFormat to application/fhir+ndjson when it is absent.
        return string.IsNullOrWhiteSpace(outputFormat)
               || outputFormat.Contains("ndjson", StringComparison.OrdinalIgnoreCase);
    }

    private void LogSkippedManifestFiles(
        FhirBulkExportManifest manifest)
    {
        //The outcome and deleted files hold OperationOutcome resources and transaction Bundles rather than the
        //exported resources, so they are not part of the output of this method.
        int outcomeFileCount = (manifest.Outcome?.Count ?? 0) + (manifest.Error?.Count ?? 0);
        if (outcomeFileCount > 0)
        {
            logger.LogWarning(
                "The Output Manifest listed {OutcomeFileCount} outcome file(s), which {MethodName} does not read.",
                outcomeFileCount,
                nameof(GetExport));
        }

        if (manifest.Deleted is { Count: > 0 })
        {
            logger.LogWarning(
                "The Output Manifest listed {DeletedFileCount} deleted file(s), which {MethodName} does not read.",
                manifest.Deleted.Count,
                nameof(GetExport));
        }
    }

    private static Uri GetBaseAddress(
        HttpClient httpClient)
    {
        Uri baseAddress = httpClient.BaseAddress                                                                                                                                                                                            
                          ?? throw new FhirBulkExportException(                                                                                                                                                                                           
                              $"The {HttpClientType.ProviderConnectAustralia} HttpClient has no BaseAddress configured.");
        if (!baseAddress.AbsolutePath.EndsWith('/'))
        {
            baseAddress = new Uri($"{baseAddress.AbsoluteUri}/");
        }

        return baseAddress;
    }


    private FhirBulkExportManifest DeserializeManifest(
        string json)
    {
        FhirBulkExportManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<FhirBulkExportManifest>(json, ManifestJsonSerializerOptions);
        }
        catch (JsonException jsonException)
        {
            logger.LogError(
                jsonException,
                "Unable to deserialize the ${ExportPollStatusOperationName} operation's Output Manifest response body: {ResponseBody}",
                ExportPollStatusOperationName,
                json);

            throw new FhirBulkExportException(
                $"Unable to deserialize the ${ExportPollStatusOperationName} operation's Output Manifest response body.",
                jsonException);
        }

        if (manifest is null)
        {
            throw new FhirBulkExportException(
                $"The ${ExportPollStatusOperationName} operation responded with an empty Output Manifest response body.");
        }

        return manifest;
    }

    private FhirBulkExportState GetFhirOperationExceptionResponse(
        FhirOperationException fhirOperationException)
    {
        logger.LogError(fhirOperationException, "The {ExportOperationName} operation encountered an error",ExportOperationName);
        CurrentSessionStatus = FhirBulkExportSessionStatus.Error;
        StartTime = null;
        EndTime = null;
        JobId = null;
        Manifest = null;

        ErrorMessages = [fhirOperationException.Message];
        if (fhirOperationException.Outcome is not null)
        {
            OperationOutcome = fhirOperationException.Outcome;
            ErrorMessages = OperationOutcomeSupport.ExtractErrorMessages(fhirOperationException.Outcome);
        }

        return GetFhirBulkExportState();
 
    }
    
    private FhirBulkExportState GetFhirBulkExportState()
    {
        return new FhirBulkExportState(
            SessionStatus: CurrentSessionStatus, 
            StartTime: StartTime, 
            EndTime: EndTime, 
            JobId: JobId,
            OperationOutcome: OperationOutcome,
            ErrorMessages: ErrorMessages,
            Manifest: Manifest);
    }

    private async Task<FhirBulkExportState> GetSessionInErrorStatus(
        FhirClient fhirClient,
        string? httpStatus)
    {
        CurrentSessionStatus = FhirBulkExportSessionStatus.Error;
        StartTime = null;
        EndTime = null;
        JobId = null;
        Manifest = null;

        if (fhirClient.LastBodyAsResource is OperationOutcome operationOutcome)
        {
            logger.LogError(
                "The ${ExportOperationName} operation responded with HTTP status {HttpStatus}, with OperationOutcome: {OperationOutcome}",
                ExportOperationName,
                httpStatus ?? "[None]",
                await operationOutcome.ToJsonAsync(new FhirJsonSerializationSettings { Pretty = true }));

            OperationOutcome = operationOutcome;
            ErrorMessages = OperationOutcomeSupport.ExtractErrorMessages(operationOutcome);

            return GetFhirBulkExportState();

        }

        if (fhirClient.LastBodyAsText is not null)
        {
            logger.LogError(
                "The ${ExportOperationName} operation responded with HTTP status {HttpStatus}, and the Body: {ResponseBody}",
                ExportOperationName,
                httpStatus ?? "[None]",
                fhirClient.LastBodyAsText);

            OperationOutcome = null;
            ErrorMessages = [fhirClient.LastBodyAsText];
            
            return GetFhirBulkExportState();
        }
            
        logger.LogError(
            "The ${ExportOperationName} operation responded with HTTP status {HttpStatus}, and no Body",
            ExportOperationName,
            httpStatus ?? "[None]");
            
        OperationOutcome = null;
        ErrorMessages =
            [$"The ${ExportOperationName} operation responded with HTTP status {httpStatus ?? "[None]"}, and no Body"];
            
        return GetFhirBulkExportState();
        
    }

    private static void SetOperationRequiredHeaders(
        FhirClient fhirClient)
    {
        ArgumentNullException.ThrowIfNull(fhirClient.RequestHeaders);
        fhirClient.RequestHeaders.Add(name: "Prefer", value: "respond-async");
    }

    private static bool IsAcceptedStatus(
        string? status)
    {
        return "202".Equals(status);
    }

    private static string GetJobId(
        string? locationHeaderValue)
    {
        if (string.IsNullOrWhiteSpace(locationHeaderValue))
        {
            throw new FhirBulkExportException(
                $"The {ExportOperationName} operation response did not contain a Content-Location header.");
        }

        //The Location header can be absolute or relative, so only parse from the query string onwards.
        //e.g. https://sit.healthconnect.digitalhealth.gov.au/adha/hcd-api-router/api/v1/fhir/$export-poll-status?_jobId=dca03739-4b96-41dd-bcaf-d6d4299d125c
        int queryIndex = locationHeaderValue.IndexOf('?');
        string query = queryIndex < 0 ? string.Empty : locationHeaderValue[(queryIndex + 1)..];

        string? jobId = HttpUtility.ParseQueryString(query)[JobIdParameterName];
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new FhirBulkExportException(
                $"Unable to locate the {JobIdParameterName} search parameter in the {ExportOperationName} operation's " +
                $"response Content-Location header of: {locationHeaderValue}");
        }

        return jobId;
    }
    
    
}