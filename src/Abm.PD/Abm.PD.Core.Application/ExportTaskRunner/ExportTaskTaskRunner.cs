using Abm.PD.BulkExport;
using Abm.PD.BulkExport.Models;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;

namespace Abm.PD.Core.Application.ExportTaskRunner;

public class ExportTaskTaskRunner(
    ILogger<ExportTaskTaskRunner> logger,
    IFhirExporter fhirExporter,
    ISourceResourceLoader sourceResourceLoader) : IExportTaskRunner
{
    public async Task<SourceResourceLoadResult> Run(
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        // Our own identifier for this run, independent of the FHIR bulk export server's JobId below -
        // see the doc comment on TaskBase.LastCorrelationId.
        Guid correlationId = Guid.CreateVersion7();
        exportTask.LastCorrelationId = correlationId;

        Parameters parameters = FhirExportQuery.FromParameter(exportTask.Parameter);

        FhirBulkExportManifest? fhirBulkExportManifest =
            await fhirExporter.RequestDownloadManifest(parameters, cancellationToken);

        ArgumentNullException.ThrowIfNull(fhirBulkExportManifest);
        ArgumentNullException.ThrowIfNull(fhirExporter.JobId);

        logger.LogInformation(
            "CorrelationId {CorrelationId} JobId {JobId} ExportTask {TaskCode} download manifest received, persisting to source store",
            correlationId,
            fhirExporter.JobId,
            exportTask.Code);

        return await sourceResourceLoader.Load(
            exportResources: fhirExporter.StreamedExportFileList(cancellationToken),
            correlationId: correlationId,
            dataSource: exportTask.DataSource,
            cancellationToken: cancellationToken);
    }
}
