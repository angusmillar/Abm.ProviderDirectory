using Abm.PD.Domain.FhirBulkExport;

namespace Abm.PD.Domain.Writer;

public interface IFhirDiskWriter
{
    Task Load(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        CancellationToken cancellationToken);
}