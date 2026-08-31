using FhirNavigator.FhirHttpClient;
using Hl7.Fhir.Rest;

namespace Abm.PD.Tests.TestDoubles;

/// <summary>
/// An <see cref="IFhirHttpClientFactory"/> that hands out a real Firely <see cref="FhirClient"/> whose transport
/// is the test's <see cref="StubHttpMessageHandler"/>.
///
/// The FhirClient itself is deliberately not faked: it is the piece doing the FHIR serialisation, the status
/// code checking and the LastResult/LastBodyAsResource bookkeeping that FhirBulkExporter reads, so a fake of it
/// would only test the fake. Replacing the handler underneath it keeps all of that real while removing the
/// network.
/// </summary>
public sealed class StubFhirHttpClientFactory(
    FhirClient fhirClient) : IFhirHttpClientFactory
{
    private readonly List<string> RequestedNames = [];

    public IReadOnlyList<string> RequestedClientNames => RequestedNames;

    public FhirClient CreateClient(
        string orderRepositoryCode)
    {
        RequestedNames.Add(orderRepositoryCode);
        return fhirClient;
    }
}
