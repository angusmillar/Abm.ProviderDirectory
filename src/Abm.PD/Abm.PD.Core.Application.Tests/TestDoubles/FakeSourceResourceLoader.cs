using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

public sealed class FakeSourceResourceLoader : ISourceResourceLoader
{
    public List<FhirBulkExportResource> ReceivedResources { get; } = [];

    public string? ReceivedJobId { get; private set; }

    public DataSource? ReceivedDataSource { get; private set; }

    public SourceResourceLoadResult ResultToReturn { get; set; } =
        new(SubmittedCount: 0, CommittedCount: 0, FailedCount: 0, BatchCount: 0, RetainedFailures: []);

    public async Task<SourceResourceLoadResult> Load(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        string jobId,
        DataSource dataSource,
        CancellationToken cancellationToken)
    {
        ReceivedJobId = jobId;
        ReceivedDataSource = dataSource;

        await foreach (FhirBulkExportResource resource in exportResources.WithCancellation(cancellationToken))
        {
            ReceivedResources.Add(resource);
        }

        return ResultToReturn;
    }
}
