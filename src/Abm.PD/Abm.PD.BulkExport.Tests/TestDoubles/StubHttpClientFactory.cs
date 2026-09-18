namespace Abm.PD.BulkExport.Tests.TestDoubles;

/// <summary>
/// An <see cref="IHttpClientFactory"/> standing in for the named clients FhirNavigator registers, built over the
/// test's <see cref="StubHttpMessageHandler"/>.
///
/// A fresh <see cref="HttpClient"/> is minted on every <see cref="CreateClient"/> call, matching the real
/// IHttpClientFactory's guarantee that each call hands back its own wrapper over a pooled handler — production
/// code (FhirBulkExporter.GetExport) relies on that guarantee to set HttpClient.Timeout on the instance it was
/// just given, which is only safe when no other caller could already have sent a request on it.
/// </summary>
public sealed class StubHttpClientFactory(
    HttpMessageHandler handler,
    Uri? baseAddress) : IHttpClientFactory
{
    private readonly List<string> RequestedNames = [];
    private readonly List<HttpClient> CreatedClientsList = [];

    /// <summary>
    /// The repository codes the factory has been asked for, so a test can assert the exporter keys its clients
    /// by the expected repository code.
    /// </summary>
    public IReadOnlyList<string> RequestedClientNames => RequestedNames;

    /// <summary>
    /// Every HttpClient the factory has handed out, in order, so a test can inspect what CreateClient actually
    /// returned — e.g. whether GetExport set an extended Timeout on it.
    /// </summary>
    public IReadOnlyList<HttpClient> CreatedClients => CreatedClientsList;

    public HttpClient CreateClient(
        string name)
    {
        RequestedNames.Add(name);

        HttpClient client = new(handler, disposeHandler: false) { BaseAddress = baseAddress };
        CreatedClientsList.Add(client);
        return client;
    }
}
