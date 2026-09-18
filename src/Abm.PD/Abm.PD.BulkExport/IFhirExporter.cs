using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Models;
using Hl7.Fhir.Model;

namespace Abm.PD.BulkExport;

public interface IFhirExporter
{
    /// <summary>
    /// The current export session's server-assigned job id, populated once <see cref="RequestDownloadManifest"/>
    /// has been called. Null before then.
    /// </summary>
    string? JobId { get; }

    Task<FhirBulkExportManifest?> RequestDownloadManifest(
        Parameters parameters,
        string repositoryCode,
        CancellationToken cancellationToken);

    IAsyncEnumerable<FhirBulkExportResource> StreamedExportFileList(
        CancellationToken cancellationToken);
}