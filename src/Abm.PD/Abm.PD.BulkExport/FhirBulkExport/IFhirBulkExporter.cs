using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.BulkExport.FhirBulkExport;

public interface IFhirBulkExporter
{
    Task<FhirBulkExportState> BeginExport(Parameters parameters, string repositoryCode, CancellationToken cancellationToken);

    Task<FhirBulkExportState> PollExport(
        CancellationToken cancellationToken);

    Task<FhirBulkExportState> DeleteExport(
        CancellationToken cancellationToken);
    
    IAsyncEnumerable<FhirBulkExportResource> GetExport(
        CancellationToken cancellationToken);
}
