using Abm.PD.Tests.TestData;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Tests.TestDoubles;

/// <summary>
/// Guards the guard. The whole battery depends on <see cref="StubHttpMessageHandler"/> refusing anything it has
/// not been told to answer, because that refusal is what turns "the test forgot a route" into a fast, obvious
/// failure instead of a request that leaves the machine.
/// </summary>
public class NetworkIsolationTests
{
    [Fact]
    public async Task AnUnscriptedRequestFailsRatherThanBeingSent()
    {
        using FhirBulkExporterHarness harness = new();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Exporter.BeginExport(new Parameters(), CancellationToken.None));

        Assert.Contains("no scripted response", exception.Message);
    }

    [Fact]
    public async Task AnUnscriptedOutputFileFailsRatherThanBeingFetched()
    {
        using FhirBulkExporterHarness harness = new();
        await harness.ArriveAtCompleted(BulkExportTestData.SingleOutputFileManifest());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (Domain.FhirBulkExport.FhirBulkExportResource _ in harness.Exporter.GetExport(CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public void TheTestServiceBaseUrlIsInAReservedNonResolvableDomain()
    {
        //RFC 2606 reserves .test, so even a request that somehow escaped the handler could not reach a real
        //server. The addresses are not a real provider directory under any circumstance.
        Assert.EndsWith(".test", new Uri(TestUrls.ServiceBaseUrl).Host);
        Assert.EndsWith(".test", new Uri(TestUrls.PractitionerOutputFileUrl).Host);
        Assert.EndsWith(".test", new Uri(TestUrls.OrganizationOutputFileUrl).Host);
    }

    [Fact]
    public void TheHandlerRecordsEveryRequestItIsAskedToSend()
    {
        using FhirBulkExporterHarness harness = new();

        Assert.Empty(harness.Handler.ReceivedRequests);
    }
}
