using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace Abm.PD.BulkExport.Tests.TestDoubles;

/// <summary>
/// Builders for the canned HTTP responses the bulk export flow expects, so the scripted routes in each test read
/// as the specification's responses rather than as HttpResponseMessage plumbing.
/// </summary>
public static class HttpResponses
{
    /// <summary>
    /// The 202 Accepted kick-off response, carrying the poll-status URL in the Location header.
    /// </summary>
    public static HttpResponseMessage KickOffAccepted(
        string? location = TestUrls.PollStatusUrlWithJobId)
    {
        HttpResponseMessage response = new(HttpStatusCode.Accepted)
        {
            Content = new StringContent(string.Empty)
        };

        if (location is not null)
        {
            response.Headers.Location = new Uri(location);
        }

        return response;
    }

    /// <summary>
    /// A 202 Accepted kick-off response whose only pointer to the job is the Content-Location header.
    /// </summary>
    public static HttpResponseMessage KickOffAcceptedWithContentLocation(
        string location = TestUrls.PollStatusUrlWithJobId)
    {
        HttpResponseMessage response = new(HttpStatusCode.Accepted)
        {
            Content = new StringContent(string.Empty)
        };
        response.Content.Headers.ContentLocation = new Uri(location);
        return response;
    }

    /// <summary>
    /// The 202 Accepted "still building" poll response, with the optional X-Progress and Retry-After headers.
    /// </summary>
    public static HttpResponseMessage PollInProgress(
        string? progress = null,
        TimeSpan? retryAfter = null)
    {
        HttpResponseMessage response = new(HttpStatusCode.Accepted);

        if (progress is not null)
        {
            response.Headers.Add("X-Progress", progress);
        }

        if (retryAfter is not null)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
        }

        return response;
    }

    /// <summary>
    /// The 200 OK poll response carrying the Output Manifest.
    /// </summary>
    public static HttpResponseMessage PollComplete(
        string manifestJson)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// An NDJSON output file response.
    /// </summary>
    public static HttpResponseMessage NdJson(
        string ndJson)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ndJson, Encoding.UTF8, "application/fhir+ndjson")
        };
    }

    /// <summary>
    /// An NDJSON output file response that is still gzip compressed and still declares Content-Encoding, as a
    /// server sends it when the client's handler has not transparently decompressed it.
    /// </summary>
    public static HttpResponseMessage GzippedNdJson(
        string ndJson)
    {
        using MemoryStream compressed = new();
        using (GZipStream gzipStream = new(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(ndJson);
            gzipStream.Write(bytes, 0, bytes.Length);
        }

        ByteArrayContent content = new(compressed.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+ndjson");
        content.Headers.ContentEncoding.Add("gzip");

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>
    /// An NDJSON output file response served over a stream that records how much of itself has been read, so a
    /// test can prove the file was not buffered into memory up front.
    /// </summary>
    public static HttpResponseMessage NdJsonOverTrackedStream(
        string ndJson,
        out ReadTrackingStream trackedStream)
    {
        trackedStream = new ReadTrackingStream(Encoding.UTF8.GetBytes(ndJson));

        StreamContent content = new(trackedStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/fhir+ndjson");

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>
    /// A batch-response Bundle answering every entry with the same status, as the target server sends when a
    /// whole batch was accepted.
    /// </summary>
    public static HttpResponseMessage BatchResponseAll(
        string status,
        int entryCount)
    {
        return BatchResponse(Enumerable.Repeat<(string, string?)>((status, null), entryCount).ToArray());
    }

    /// <summary>
    /// A batch-response Bundle with one entry per supplied status, in the order the request entries were sent.
    /// A batch answers 200 OK however the individual entries fared, so this is the only place a refused resource
    /// is reported.
    /// </summary>
    public static HttpResponseMessage BatchResponse(
        params (string Status, string? Diagnostics)[] entryList)
    {
        Bundle responseBundle = new()
        {
            Type = Bundle.BundleType.BatchResponse,
            Entry = entryList.Select(entry => new Bundle.EntryComponent
            {
                Response = new Bundle.ResponseComponent
                {
                    Status = entry.Status,
                    Outcome = entry.Diagnostics is null
                        ? null
                        : new OperationOutcome
                        {
                            Issue =
                            [
                                new OperationOutcome.IssueComponent
                                {
                                    Severity = OperationOutcome.IssueSeverity.Error,
                                    Code = OperationOutcome.IssueType.Processing,
                                    Diagnostics = entry.Diagnostics
                                }
                            ]
                        }
                }
            }).ToList()
        };

        return FhirJson(HttpStatusCode.OK, responseBundle.ToJson());
    }

    public static HttpResponseMessage FhirJson(
        HttpStatusCode statusCode,
        string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/fhir+json")
        };
    }

    public static HttpResponseMessage Empty(
        HttpStatusCode statusCode)
    {
        return new HttpResponseMessage(statusCode) { Content = new StringContent(string.Empty) };
    }
}
