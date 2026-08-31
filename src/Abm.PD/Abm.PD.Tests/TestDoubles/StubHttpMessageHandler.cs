using System.Text;

namespace Abm.PD.Tests.TestDoubles;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from a scripted route table instead of the network.
///
/// This is the single seam that keeps the whole test battery off the live provider directory. A handler sits at
/// the bottom of every <see cref="HttpClient"/> pipeline, so replacing it removes the transport altogether — no
/// socket is opened, no DNS lookup is made, and the tests run identically on a machine with no network at all.
///
/// A request that matches no route throws rather than falling through to a real send, so a route a test forgot
/// to script fails loudly and immediately instead of hanging on a connection attempt.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Route> Routes = [];
    private readonly List<RecordedRequest> Requests = [];

    /// <summary>
    /// Every request the handler has been asked to send, in order.
    /// </summary>
    public IReadOnlyList<RecordedRequest> ReceivedRequests => Requests;

    /// <summary>
    /// Scripts a response for the requests matched by <paramref name="predicate"/>. The response is built per
    /// call so that a route can be hit more than once and still hand back a readable body each time.
    /// </summary>
    public StubHttpMessageHandler RespondTo(
        Func<HttpRequestMessage, bool> predicate,
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        Routes.Add(new Route(predicate, respond));
        return this;
    }

    /// <summary>
    /// Scripts a response for requests of the given method whose path and query contains
    /// <paramref name="pathAndQueryContains"/>.
    /// </summary>
    public StubHttpMessageHandler RespondTo(
        HttpMethod method,
        string pathAndQueryContains,
        Func<HttpResponseMessage> respond)
    {
        return RespondTo(
            predicate: request => request.Method == method
                                  && request.RequestUri is not null
                                  && request.RequestUri.PathAndQuery.Contains(pathAndQueryContains, StringComparison.Ordinal),
            respond: _ => respond());
    }

    /// <summary>
    /// Scripts a response for an absolute URL, as used by the manifest's output file entries.
    /// </summary>
    public StubHttpMessageHandler RespondToUrl(
        string url,
        Func<HttpResponseMessage> respond)
    {
        Uri expected = new(url);
        return RespondTo(
            predicate: request => request.RequestUri == expected,
            respond: _ => respond());
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Requests.Add(await RecordAsync(request, cancellationToken));

        foreach (Route route in Routes)
        {
            if (route.Predicate(request))
            {
                HttpResponseMessage response = route.Respond(request);
                response.RequestMessage = request;
                return response;
            }
        }

        throw new InvalidOperationException(
            $"The test's StubHttpMessageHandler has no scripted response for {request.Method} {request.RequestUri}. " +
            "No request is ever allowed to leave the test process, so this is a missing route in the test, not a " +
            "network problem.");
    }

    private static async Task<RecordedRequest> RecordAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        //The body has to be read now — by the time the test asserts, the request message has been disposed.
        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return new RecordedRequest(
            Method: request.Method,
            RequestUri: request.RequestUri ?? throw new InvalidOperationException("The request had no RequestUri."),
            Headers: request.Headers,
            ContentHeaders: request.Content?.Headers,
            Body: body);
    }

    private sealed record Route(
        Func<HttpRequestMessage, bool> Predicate,
        Func<HttpRequestMessage, HttpResponseMessage> Respond);
}
