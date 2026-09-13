using Abm.PD.BulkExport;
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Models;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application;

public class ExportRunner(
    ILogger<ExportRunner> logger,
    IFhirExporter fhirExporter,
    ISourceResourceLoader sourceResourceLoader) : IExportRunner
{
    public async Task<SourceResourceLoadResult> Run(
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        Parameters parameters = FhirExportQuery.FromParameter(exportLoaderTask.Parameter);

        FhirBulkExportManifest? fhirBulkExportManifest =
            await fhirExporter.RequestDownloadManifest(parameters, cancellationToken);

        ArgumentNullException.ThrowIfNull(fhirBulkExportManifest);
        ArgumentNullException.ThrowIfNull(fhirExporter.JobId);

        logger.LogInformation(
            "JobId {JobId} ExportLoaderTask {TaskCode} download manifest received, persisting to source store",
            fhirExporter.JobId,
            exportLoaderTask.Code);

        IAsyncEnumerable<FhirBulkExportResource> streamedExportFileList = fhirExporter.StreamedExportFileList(cancellationToken);

        return await sourceResourceLoader.Load(
            exportResources: streamedExportFileList,
            jobId: fhirExporter.JobId,
            dataSource: exportLoaderTask.DataSource,
            cancellationToken: cancellationToken);
    }
}
