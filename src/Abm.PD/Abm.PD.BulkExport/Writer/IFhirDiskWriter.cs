using Abm.PD.BulkExport.FhirBulkExport;

namespace Abm.PD.BulkExport.Writer;

public interface IFhirDiskWriter
{
    DirectoryInfo OutputDirectoryInfo { get; }
    Task Write(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        CancellationToken cancellationToken);
}