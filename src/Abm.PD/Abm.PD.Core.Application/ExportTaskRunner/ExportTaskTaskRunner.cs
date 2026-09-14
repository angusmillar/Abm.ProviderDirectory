using Abm.PD.BulkExport;
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Models;
using Abm.PD.Core.Application.Loader;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;

namespace Abm.PD.Core.Application.ExportTaskRunner;

public class ExportTaskTaskRunner(
    ILogger<ExportTaskTaskRunner> logger,
    IFhirExporter fhirExporter,
    ISourceResourceLoader sourceResourceLoader) : IExportTaskRunner
{
    public async Task<SourceResourceLoadResult> Run(
        Domain.Entities.ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        Parameters parameters = FhirExportQuery.FromParameter(exportTask.Parameter);

        FhirBulkExportManifest? fhirBulkExportManifest =
            await fhirExporter.RequestDownloadManifest(parameters, cancellationToken);

        ArgumentNullException.ThrowIfNull(fhirBulkExportManifest);
        ArgumentNullException.ThrowIfNull(fhirExporter.JobId);

        logger.LogInformation(
            "JobId {JobId} ExportTask {TaskCode} download manifest received, persisting to source store",
            fhirExporter.JobId,
            exportTask.Code);

        IAsyncEnumerable<FhirBulkExportResource> streamedExportFileList = fhirExporter.StreamedExportFileList(cancellationToken);

        return await sourceResourceLoader.Load(
            exportResources: streamedExportFileList,
            jobId: fhirExporter.JobId,
            dataSource: exportTask.DataSource,
            cancellationToken: cancellationToken);
    }
}
