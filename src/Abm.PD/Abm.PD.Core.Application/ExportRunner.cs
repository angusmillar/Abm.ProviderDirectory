using Abm.PD.BulkExport;
using Abm.PD.BulkExport.Loader;
using Abm.PD.BulkExport.Models;
using Abm.PD.Core.Domain.Entities;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application;

public class ExportRunner(
    ILogger<ExportRunner> logger,
    IFhirExporter fhirExporter,
    IFhirBatchLoader fhirBatchLoader) : IExportRunner
{
    public async Task<FhirBatchLoadResult> Run(
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        Parameters parameters = FhirExportQuery.FromParameter(exportLoaderTask.Parameter);

        FhirBulkExportManifest? fhirBulkExportManifest =
            await fhirExporter.RequestDownloadManifest(parameters, cancellationToken);

        ArgumentNullException.ThrowIfNull(fhirBulkExportManifest);

        logger.LogInformation("ExportLoaderTask {TaskCode} download manifest received, loading into target", exportLoaderTask.Code);

        return await fhirBatchLoader.Load(
            exportResources: fhirExporter.StreamedExportFileList(cancellationToken),
            cancellationToken: cancellationToken);
    }
}
