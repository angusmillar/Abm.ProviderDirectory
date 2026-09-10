using Abm.PD.Domain.FhirBulkExport;

namespace Abm.PD.Domain.Writer;

public interface IFhirDiskWriter
{
    DirectoryInfo OutputDirectoryInfo { get; }
    Task Write(
        IAsyncEnumerable<FhirBulkExportResource> exportResources,
        CancellationToken cancellationToken);
}