namespace Abm.PD.Tests.TestDoubles;

/// <summary>
/// An <see cref="IHttpClientFactory"/> that hands out a single <see cref="HttpClient"/> built over the test's
/// <see cref="StubHttpMessageHandler"/>, standing in for the named clients FhirNavigator registers.
/// </summary>
public sealed class StubHttpClientFactory(
    HttpClient httpClient) : IHttpClientFactory
{
    private readonly List<string> RequestedNames = [];

    /// <summary>
    /// The repository codes the factory has been asked for, so a test can assert the exporter keys its clients
    /// by the expected HttpClientType constant.
    /// </summary>
    public IReadOnlyList<string> RequestedClientNames => RequestedNames;

    public HttpClient CreateClient(
        string name)
    {
        RequestedNames.Add(name);
        return httpClient;
    }
}
