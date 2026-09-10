using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Models.Manifest;
using Hl7.Fhir.Model;

namespace Abm.PD.BulkExport;

public interface IFhirExporter
{
    Task<FhirBulkExportManifest?> RequestDownloadManifest(
        Parameters parameters,
        CancellationToken cancellationToken);

    IAsyncEnumerable<FhirBulkExportResource> StreamedExportFileList(
        CancellationToken cancellationToken);
}