using System.Runtime.CompilerServices;
using Abm.PD.BulkExport;
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Models;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

public sealed class FakeFhirExporter : IFhirExporter
{
    public FhirBulkExportManifest? ManifestToReturn { get; set; } = new FhirBulkExportManifest
    {
        TransactionTime = DateTimeOffset.UtcNow,
        RequiresAccessToken = false,
    };

    public List<FhirBulkExportResource> ResourcesToStream { get; set; } = [];

    public Parameters? ReceivedParameters { get; private set; }

    public Task<FhirBulkExportManifest?> RequestDownloadManifest(
        Parameters parameters,
        CancellationToken cancellationToken)
    {
        ReceivedParameters = parameters;
        return Task.FromResult(ManifestToReturn);
    }

    public async IAsyncEnumerable<FhirBulkExportResource> StreamedExportFileList(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (FhirBulkExportResource resource in ResourcesToStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return resource;
            await Task.Yield();
        }
    }
}
