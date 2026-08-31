using System.Net.Http.Headers;

namespace Abm.PD.Tests.TestDoubles;

/// <summary>
/// A copy of one request that reached the <see cref="StubHttpMessageHandler"/>, taken before the request message
/// is disposed so that a test can assert on it after the call has returned.
/// </summary>
public sealed record RecordedRequest(
    HttpMethod Method,
    Uri RequestUri,
    HttpRequestHeaders Headers,
    HttpContentHeaders? ContentHeaders,
    string? Body)
{
    public bool HasHeader(
        string name,
        string value)
    {
        return Headers.TryGetValues(name, out IEnumerable<string>? values)
               && values.Any(headerValue => headerValue.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string PathAndQuery => RequestUri.PathAndQuery;
}
