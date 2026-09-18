using Abm.PD.BulkExport;
using Abm.PD.BulkExport.Models;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Application.Settings;
using Abm.PD.Core.Domain.Entities;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Abm.PD.Core.Application.ExportTaskRunner;

public class ExportTaskRunner(
    ILogger<ExportTaskRunner> logger,
    IOptions<ProviderDirectorySettings>  providerDirectorySettings,
    IFhirExporter fhirExporter,
    ISourceResourceLoader sourceResourceLoader) : IExportTaskRunner
{
    
    public async Task<SourceResourceLoadResult> Run(
        ExportTask exportTask,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Running {TaskType}: {DisplayName} with CorrelationId {CorrelationId} ",
            exportTask.TypeId,
            exportTask.DisplayName,
            correlationId);
        
        Parameters parameters = FhirExportQuery.FromParameter(exportTask.Parameter);

        FhirBulkExportManifest? fhirBulkExportManifest =
            await fhirExporter.RequestDownloadManifest(parameters, providerDirectorySettings.Value.FhirRepositoryCodeAssignment.HealthConnectProviderDirectorySource, cancellationToken);

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
