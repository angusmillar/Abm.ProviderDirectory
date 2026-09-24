using Abm.PD.BulkExport.FhirBulkExport;

namespace Abm.PD.BulkExport.Loader;

public interface IFhirTransactionLoader
{
    Task<FhirBatchLoadResult> Load(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        string repositoryCode,
        CancellationToken cancellationToken);
}
