using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Abm.PD.Tests.TestDoubles;

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
