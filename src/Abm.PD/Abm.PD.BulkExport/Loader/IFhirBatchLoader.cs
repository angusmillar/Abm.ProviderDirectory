using Abm.PD.BulkExport.FhirBulkExport;

namespace Abm.PD.BulkExport.Loader;

public interface IFhirBatchLoader
{
    Task<FhirBatchLoadResult> Load(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        string repositoryCode,
        CancellationToken cancellationToken);
}
