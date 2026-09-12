using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Loader;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

public sealed class FakeFhirBatchLoader : IFhirBatchLoader
{
    public List<FhirBulkExportResource> ReceivedResources { get; } = [];

    public FhirBatchLoadResult ResultToReturn { get; set; } =
        new(SubmittedCount: 0, CommittedCount: 0, FailedCount: 0, BatchCount: 0, RetainedFailures: []);

    public async Task<FhirBatchLoadResult> Load(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        CancellationToken cancellationToken)
    {
        await foreach (FhirBulkExportResource resource in exportResources.WithCancellation(cancellationToken))
        {
            ReceivedResources.Add(resource);
        }

        return ResultToReturn;
    }
}
