using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Abm.PD.Tests.TestDoubles;

/// <summary>
/// An <see cref="HttpContent"/> that does not produce its body until the test releases it.
///
/// This is how a slow target server is simulated without a real one: the response headers come back at once, but
/// the FhirClient's read of the body parks until <see cref="Release"/> is called, so the test controls exactly
/// how long a batch commit is in flight.
/// </summary>
public sealed class GatedHttpContent : HttpContent
{
    private readonly byte[] Payload;

    private readonly TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public GatedHttpContent(
        string body,
        string mediaType = "application/fhir+json")
    {
        Payload = Encoding.UTF8.GetBytes(body);
        Headers.ContentType = new MediaTypeHeaderValue(mediaType);
    }

    /// <summary>
    /// Lets the body be written, completing the request that was waiting on it.
    /// </summary>
    public void Release()
    {
        Gate.TrySetResult();
    }

    public static HttpResponseMessage Response(
        string body,
        out GatedHttpContent gatedContent)
    {
        gatedContent = new GatedHttpContent(body);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = gatedContent };
    }

    protected override async Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        await Gate.Task;
        await stream.WriteAsync(Payload);
    }

    protected override bool TryComputeLength(
        out long length)
    {
        length = Payload.Length;
        return true;
    }
}
