using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Domain.FhirBulkExport;

public interface IFhirBulkExporter
{
    Task<FhirBulkExportState> BeginExport(Parameters parameters, CancellationToken cancellationToken);

    Task<FhirBulkExportState> PollExport(
        CancellationToken cancellationToken);

    Task<FhirBulkExportState> DeleteExport(
        CancellationToken cancellationToken);
    
    IAsyncEnumerable<FhirBulkExportResource> GetExport(
        CancellationToken cancellationToken);
}
