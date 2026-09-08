using Abm.PD.Domain.FhirBulkExport;

namespace Abm.PD.Domain.Loader;

public interface IFhirBatchLoader
{
    Task<FhirBatchLoadResult> Load(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        CancellationToken cancellationToken);
}
