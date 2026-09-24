using System.Text;
using Abm.Core.Enums;
using Abm.Core.Time;
using Abm.PD.BulkExport;
using Abm.PD.BulkExport.Loader;
using Abm.PD.BulkExport.Models;
using Abm.PD.BulkExport.Writer;
using Abm.PD.Core.Application.FhirTaskDispatcher;
using Abm.PD.Core.Application.Identifers;
using Abm.PD.Core.Application.Settings;
using FhirNavigator;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.SeedProviderDirectoryTask;

public class SeedProviderDirectoryTaskHandler(
    ILogger<SeedProviderDirectoryTaskHandler> logger,
    IOptions<ProviderDirectorySettings> providerDirectorySettings,
    IFhirExporter fhirExporter,
    IFhirBatchLoader fhirBatchLoader,
    IFhirDiskWriter fhirDiskWriter) : ITaskHandler
{
    
    private string? ErrorMessage;

    public async Task<TaskHandlerOutcome> Handle(
        Hl7.Fhir.Model.Task task,
        IFhirNavigator fhirNavigator,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        List<Hl7.Fhir.Model.Task.ParameterComponent> parameterComponent = task.Input.Where(x =>
            HasCode(x.Type, ProviderDirectoryTaskInputType.FhirBulkExportRequestParameters.GetCode(),
                ProviderDirectoryTaskInputTypeSystem.Uri)).ToList();

        if (parameterComponent.Count == 0)
        {
            return new TaskHandlerOutcome(TaskStatus: Hl7.Fhir.Model.Task.TaskStatus.Failed,
                StatusReason:
                $"Where Task.Code is {FhirTaskHandlerType.SeedProviderDirectory.GetCode()} a Task.input.type " +
                $"of {ProviderDirectoryTaskInputType.FhirBulkExportRequestParameters.GetCode()} must be provided");
        }

        Parameters? parameters = await GetBulkExportParametersResource(task, fhirNavigator);
        if (parameters is null)
        {
            return new TaskHandlerOutcome(
                TaskStatus: Hl7.Fhir.Model.Task.TaskStatus.Failed,
                StatusReason: ErrorMessage);
        }

        FhirBulkExportManifest? fhirBulkExportManifest =
            await fhirExporter.RequestDownloadManifest(parameters, providerDirectorySettings.Value
                .FhirRepositoryCodeAssignment.HealthConnectProviderDirectoryExternal, cancellationToken);

        ArgumentNullException.ThrowIfNull(fhirBulkExportManifest);
        ArgumentNullException.ThrowIfNull(fhirExporter.JobId);

        logger.LogInformation(
            "CorrelationId {CorrelationId} JobId {JobId} download manifest received",
            correlationId,
            fhirExporter.JobId);

        try
        {
            // logger.LogInformation("== Begin Disk Write =============================================================");
            // await fhirDiskWriter.Write(exportResources: fhirExporter.StreamedExportFileList(cancellationToken),
            //     cancellationToken: cancellationToken);

            //Load exported resource into a target FHIR Server
            //The export stream is handed straight to the loader: it batches the resources as they arrive and never
            //holds more than two batches, so the resources go from the download to the target server without ever
            //being collected in full or written to disk.
            logger.LogInformation(
                "== Begin Resource Load =============================================================");
            FhirBatchLoadResult loadResult = await fhirBatchLoader.Load(
                exportResources: fhirExporter.StreamedExportFileList(cancellationToken),
                repositoryCode: fhirNavigator.RepositorySettings.Code,
                cancellationToken: cancellationToken);
            LogFhirBatchLoadResult(loadResult);

            if (loadResult.FailedCount > 0)
            {
                return new TaskHandlerOutcome(
                    TaskStatus: Hl7.Fhir.Model.Task.TaskStatus.Failed,
                    StatusReason: GetFailedFhirBatchLoadErrorMessage(loadResult));
            }

            return new TaskHandlerOutcome(
                TaskStatus: Hl7.Fhir.Model.Task.TaskStatus.Completed,
                StatusReason: GetSuccessfulFhirBatchLoadMessage(loadResult));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error loading exported resources into the target FHIR server: " +
                               "{TargetFhirServerName}", fhirNavigator.RepositorySettings.Code);

            return new TaskHandlerOutcome(
                TaskStatus: Hl7.Fhir.Model.Task.TaskStatus.Failed,
                StatusReason: $"Error loading exported resources into the target FHIR server: " +
                              $"{fhirNavigator.RepositorySettings.Code}. Exception: {e.Message}");
        }
    }

    private async Task<Parameters?> GetBulkExportParametersResource(Hl7.Fhir.Model.Task task, IFhirNavigator fhirNavigator)
    {
        List<Hl7.Fhir.Model.Task.ParameterComponent> parameterComponent = task.Input.Where(x =>
            HasCode(x.Type, ProviderDirectoryTaskInputType.FhirBulkExportRequestParameters.GetCode(),
                ProviderDirectoryTaskInputTypeSystem.Uri)).ToList();

        if (parameterComponent.Count == 0)
        {
            ErrorMessage =
                $"Where Task.Code is {FhirTaskHandlerType.SeedProviderDirectory.GetCode()} a Task.input.type " +
                $"of {ProviderDirectoryTaskInputType.FhirBulkExportRequestParameters.GetCode()} must be provided";

            return null;
        }

        if (parameterComponent.First().Value is not ResourceReference resourceReference)
        {
            ErrorMessage =
                $"Where Task.Code is {FhirTaskHandlerType.SeedProviderDirectory.GetCode()} a Task.input.type " +
                $"of {ProviderDirectoryTaskInputType.FhirBulkExportRequestParameters.GetCode()} must be provided " +
                $"with a ResourceReference to a contained Parameters resource.";

            return null;
        }
        
        Parameters? parameters = await fhirNavigator.GetResource<Parameters>(
            resourceReference: resourceReference,
            errorLocationDisplay: "Task.input.value",
            parentResource: task);

        if (parameters is null)
        {
            ErrorMessage =
                $"Where Task.Code is {FhirTaskHandlerType.SeedProviderDirectory.GetCode()} a Task.input.type " +
                $"of {ProviderDirectoryTaskInputType.FhirBulkExportRequestParameters.GetCode()} must be provided " +
                $"with a ResourceReference to a contained Parameters resource reference. Unable to find the conatined " +
                $"resource from based on the ResourceReference: {resourceReference.Reference}";

            return null;
        }

        return parameters;
    }

    private bool HasCode(
        CodeableConcept? codeableConcept,
        string code,
        Uri systemUri)
    {
        if (codeableConcept is null)
        {
            return false;
        }

        return codeableConcept.Coding.Any(x =>
            x.System.Equals(systemUri.OriginalString, StringComparison.OrdinalIgnoreCase) &&
            x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    }

    private void LogFhirBatchLoadResult(
        FhirBatchLoadResult loadResult)
    {
        logger.LogInformation(
            "Committed {CommittedCount} of {SubmittedCount} resource(s) over {BatchCount} batch(es), " +
            "{FailedCount} failed",
            loadResult.CommittedCount,
            loadResult.SubmittedCount,
            loadResult.BatchCount,
            loadResult.FailedCount);

        //Only the first few failures are retained, so say when the list is not the whole story.
        if (loadResult.FailedCount > loadResult.RetainedFailures.Count)
        {
            logger.LogWarning(
                "Only the first {RetainedFailureCount} of {FailedCount} failure(s) are listed here, the rest are " +
                "in the log above",
                loadResult.RetainedFailures.Count,
                loadResult.FailedCount);
        }

        foreach (FhirBatchLoadFailure failure in loadResult.RetainedFailures)
        {
            logger.LogWarning(
                "Failed {ResourceType}/{ResourceId} from line {LineNumber} of {SourceUrl}: {ErrorMessages}",
                failure.ResourceType,
                failure.ResourceId ?? "[None]",
                failure.LineNumber,
                failure.SourceUrl,
                string.Join("; ", failure.ErrorMessages));
        }
    }

    private string GetSuccessfulFhirBatchLoadMessage(
        FhirBatchLoadResult loadResult)
    {
        return
            $"Committed {loadResult.CommittedCount} of {loadResult.SubmittedCount} resource(s) over " +
            $"{loadResult.BatchCount} batch(es), {loadResult.FailedCount} failed";
    }

    private string GetFailedFhirBatchLoadErrorMessage(
        FhirBatchLoadResult loadResult)
    {
        StringBuilder sb = new StringBuilder(
            $"Only the first {loadResult.RetainedFailures.Count} of {loadResult.FailedCount} " +
            $"failure(s) are listed here, see application logs for all. ");

        foreach (FhirBatchLoadFailure failure in loadResult.RetainedFailures)
        {
            sb.Append($"Failed {failure.ResourceType}/{failure.ResourceId} from line {failure.LineNumber} of " +
                      $"{failure.SourceUrl}: {string.Join("; ", failure.ErrorMessages)} ");
        }

        return sb.ToString();
    }
}