using Abm.PD.BulkExport;
using Abm.PD.BulkExport.Models;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application;

public class ExportRunner(
    ILogger<ExportRunner> logger,
    IFhirExporter fhirExporter) : IExportRunner
{
    public async Task Run(CancellationToken cancellationToken)
    {
        Parameters parameters = FhirExportQuery.GetByPostCode();
        
        FhirBulkExportManifest? fhirBulkExportManifest =
            await fhirExporter.RequestDownloadManifest(parameters, cancellationToken);

        ArgumentNullException.ThrowIfNull(fhirBulkExportManifest);

        fhirExporter.StreamedExportFileList(cancellationToken);

        

    }
}