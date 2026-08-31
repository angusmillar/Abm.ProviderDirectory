using Abm.PD.Domain.FhirBulkExport;
using Abm.PD.Domain.Models.Manifest;
using Hl7.Fhir.Model;

namespace Abm.PD.Domain.Exporter;

public interface IFhirExporter
{
    Task<FhirBulkExportManifest?> RequestDownloadManifest(
        Parameters parameters,
        CancellationToken cancellationToken);

    IAsyncEnumerable<FhirBulkExportResource> ExportedFileList(
        CancellationToken cancellationToken);
}