using Abm.Core.HostedService;
using Abm.PD.Core.Application.DependencyInjection;
using Abm.PD.Core.Application.Settings;
using FhirNavigator;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Abm.Core.Enums;
using Abm.Core.Time;
using Abm.PD.Core.Application.IdentifiersSystems;
using Abm.PD.Core.Domain.Enums;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.FhirTaskDispatcher;

public class FhirTaskDispatcher(
    ILogger<FhirTaskDispatcher> logger,
    IFhirNavigatorFactory fhirNavigatorFactory,
    IOptions<ProviderDirectorySettings> providerDirectorySettings,
    IFhirTaskHandlerFactory fhirTaskHandlerFactory,
    IDateTimeProvider dateTimeProvider,
    IdentifierSystemSupport identifierSystemSupport
) : ITimedHostedService
{
    // Built from the FhirTaskCodeAttribute on each FhirTaskHandlerType value, so the code-to-handler
    // association is declared once, alongside the enum, rather than duplicated in a lookup table here.
    private static readonly Dictionary<string, FhirTaskHandlerType> FhirTaskCodeTypeMap =
        StringToEnumMap<FhirTaskHandlerType>.GetDictionary();

    public async Task DoWork(
        CancellationToken cancellationToken)
    {
        string[] repositoryCodeList =
        [
            providerDirectorySettings.Value.LocalFhirRepositoryCodes.TelstraHealthProviderDirectory,
            providerDirectorySettings.Value.LocalFhirRepositoryCodes.HealthConnectProviderDirectory,
            providerDirectorySettings.Value.LocalFhirRepositoryCodes.HealthLinkProviderDirectory
        ];

        foreach (string repositoryCode in repositoryCodeList)
        {
            await DispatchRepositoryTasks(repositoryCode, cancellationToken);
        }
    }

    private async Task DispatchRepositoryTasks(
        string repositoryCode,
        CancellationToken cancellationToken)
    {
        var groupSearchParams = new SearchParams();

        groupSearchParams.Add("status", Hl7.Fhir.Model.Task.TaskStatus.Ready.GetLiteral());
        groupSearchParams.Add("_count", "100");

        IFhirNavigator fhirNavigator = fhirNavigatorFactory.GetFhirNavigator(repositoryCode);

        try
        {
            SearchInfo searchInfo = await fhirNavigator.Search<Hl7.Fhir.Model.Task>(groupSearchParams);

            logger.LogInformation(
                "{ResourceTotal} FHIR Tasks to be dispatched from the FHIR server {RepositoryCode} : {RepositoryDisplay} ",
                searchInfo.ResourceTotal,
                fhirNavigator.RepositorySettings.Code,
                fhirNavigator.RepositorySettings.DisplayName);

            var taskList = fhirNavigator.Cache.GetList<Hl7.Fhir.Model.Task>();
            fhirNavigator.Cache.Clear();

            foreach (Hl7.Fhir.Model.Task task in taskList)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (AllTaskRepetitionsPerformed(task))
                {
                    await UpdateTaskAsRepetitionsCompleted(task, fhirNavigator);
                    continue;
                }

                if (OutSideOfRestrictionPeriod(task))
                {
                    continue;
                }
                
                await DispatchFhirTask(task, fhirNavigator, cancellationToken);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error calling the FHIR Server {RepositoryCode}:{RepositoryDisplay} at {Endpoint}",
                fhirNavigator.RepositorySettings.Code,
                fhirNavigator.RepositorySettings.DisplayName,
                fhirNavigator.RepositorySettings.ServiceBaseUrl.OriginalString);
        }
    }


    private async Task DispatchFhirTask(
        Hl7.Fhir.Model.Task task,
        IFhirNavigator fhirNavigator,
        CancellationToken cancellationToken)
    {
        FhirTaskHandlerType? fhirTaskHandlerType = GetTaskType(task.Code);
        if (!fhirTaskHandlerType.HasValue)
        {
            logger.LogError(
                "Unrecognised FHIR Task/{ResourceId} found in the FHIR Server {RepositoryCode} : {RepositoryDisplay} " +
                "at {Endpoint}. The Task.Code is unknown to this processor. The Task has been ignored",
                task.Id,
                fhirNavigator.RepositorySettings.Code,
                fhirNavigator.RepositorySettings.DisplayName,
                fhirNavigator.RepositorySettings.ServiceBaseUrl.OriginalString);

            return;
        }

        Guid correlationId = Guid.CreateVersion7();
        ITaskHandler taskHandler = fhirTaskHandlerFactory.Get(fhirTaskHandlerType.Value);
        try
        {
            task = await UpdateAsInProgress(task, correlationId, fhirNavigator);
            
            TaskHandlerOutcome taskHandlerOutcome = await taskHandler.Handle(
                task: task,
                fhirNavigator: fhirNavigator,
                correlationId: correlationId,
                cancellationToken: cancellationToken);
            
            await UpdateTaskBasedOnHandlerOutcome(task, taskHandlerOutcome, fhirNavigator);
        }
        catch (Exception e)
        {
            logger.LogError(e,
                "CorrelationId: {CorrelationId} - Error processing FHIR Task/{ResourceId} from the " +
                "FHIR Server {RepositoryCode}:{RepositoryDisplay} at {Endpoint}",
                correlationId,
                task.Id,
                fhirNavigator.RepositorySettings.Code,
                fhirNavigator.RepositorySettings.DisplayName,
                fhirNavigator.RepositorySettings.ServiceBaseUrl.OriginalString);

            string errorMessage = $"Unhandled Exception, see application logs for for exception message with " +
                                  $"correlationId: {correlationId.ToString()}";
            
            await UpdateTaskAsFailed(task, errorMessage, fhirNavigator);
        }
    }

    private async Task UpdateTaskBasedOnHandlerOutcome(
        Hl7.Fhir.Model.Task task,
        TaskHandlerOutcome? taskHandlerOutcome,
        IFhirNavigator fhirNavigator)
    {
        if (taskHandlerOutcome is null)
        {
            return;
        }

        var now = dateTimeProvider.Now;
        
        var newTaskStatus = taskHandlerOutcome.TaskStatus;
        if (taskHandlerOutcome.TaskStatus == Hl7.Fhir.Model.Task.TaskStatus.Completed)
        {
            IncrementTaskRepetitionsCounter(task);
            if (!AllTaskRepetitionsPerformed(task))
            {
                newTaskStatus = Hl7.Fhir.Model.Task.TaskStatus.Ready;
            }
        }

        task.StatusReason = string.IsNullOrWhiteSpace(taskHandlerOutcome.StatusReason)
            ? null
            : new CodeableConcept() { Text = taskHandlerOutcome.StatusReason };
        SetExecutionEndPeriod(task, now);
        task.Status = newTaskStatus;
        task.LastModifiedElement = new FhirDateTime(now);
        await fhirNavigator.UpdateResource(task);
    }

    private async Task UpdateTaskAsRepetitionsCompleted(
        Hl7.Fhir.Model.Task task,
        IFhirNavigator fhirNavigator)
    {
        if (task.StatusReason is null)
        {
            task.StatusReason = new CodeableConcept() { Text = "Task Repetitions have been completed." };
        }
        else if (string.IsNullOrWhiteSpace(task.StatusReason.Text))
        {
            task.StatusReason.Text = "Task Repetitions have been completed.";
        }
        else
        {
            task.StatusReason.Text = $"{task.StatusReason.Text} All task Repetitions have been completed.";
        }

        task.Status = Hl7.Fhir.Model.Task.TaskStatus.Completed;
        task.LastModifiedElement = new FhirDateTime(dateTimeProvider.Now);
        await fhirNavigator.UpdateResource(task);
    }

    private async Task<Hl7.Fhir.Model.Task> UpdateAsInProgress(
        Hl7.Fhir.Model.Task task,
        Guid correlationId,
        IFhirNavigator fhirNavigator)
    {
        var now = dateTimeProvider.Now;
        
        task.Identifier.Add(new Identifier(
            system: identifierSystemSupport.ProviderDirectoryTaskCorrelationId.OriginalString,
            value: correlationId.ToString()));
        task.Status = Hl7.Fhir.Model.Task.TaskStatus.InProgress;
        task.StatusReason = null;
        task.LastModifiedElement = new FhirDateTime(now);
        task.ExecutionPeriod = new Period()
        {
            StartElement = new FhirDateTime(now),
        };
        
        return await fhirNavigator.UpdateResource(task);
    }

    private async Task UpdateTaskAsFailed(
        Hl7.Fhir.Model.Task task,
        string statusReason,
        IFhirNavigator fhirNavigator)
    {
        var now = dateTimeProvider.Now;
        task.Status = Hl7.Fhir.Model.Task.TaskStatus.Failed;
        task.StatusReason = new CodeableConcept() {Text = statusReason};
        task.LastModifiedElement = new FhirDateTime(now);
        SetExecutionEndPeriod(task, now);
        await fhirNavigator.UpdateResource(task);
    }

    private static void SetExecutionEndPeriod(
        Hl7.Fhir.Model.Task task,
        DateTimeOffset now)
    {
        if (task.ExecutionPeriod is null)
        {
            task.ExecutionPeriod = new Period()
            {
                EndElement = new FhirDateTime(now),
            };    
        }
        else
        {
            task.ExecutionPeriod.EndElement = new FhirDateTime(now);    
        }
    }

    private FhirTaskHandlerType? GetTaskType(
        CodeableConcept? codeableConcept)
    {
        var codeList = codeableConcept?.Coding.Where(x =>
            x.System.Equals(FhirTaskHandlerTypeSystem.Uri.OriginalString, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (KeyValuePair<string, FhirTaskHandlerType> fhirTaskHandlerType in FhirTaskCodeTypeMap)
        {
            if (codeList?.FirstOrDefault(x =>
                    x.Code.Equals(fhirTaskHandlerType.Key, StringComparison.OrdinalIgnoreCase)) != null)
            {
                return fhirTaskHandlerType.Value;
            }
        }

        return null;
    }

    private bool OutSideOfRestrictionPeriod(Hl7.Fhir.Model.Task task)
    {
        var now = dateTimeProvider.Now;
        if ((task.Restriction?.Period?.Start is not null && task.Restriction?.Period?.StartElement.ToDateTimeOffset(now.Offset) > now) ||
            (task.Restriction?.Period?.End is not null && task.Restriction?.Period?.EndElement.ToDateTimeOffset(now.Offset) > now)) 
        {
            logger.LogInformation("FHIR Task/{ResourceId} has been ignored due to its Restriction.period which " +
                                  "Starts: {Starts} and Ends: {Ends}", 
                task.Id,
                task.Restriction.Period.StartElement.ToDateTimeOffset(now.Offset),
                task.Restriction.Period.EndElement.ToDateTimeOffset(now.Offset));
            return false;
        }

        return true;
    }

    private bool AllTaskRepetitionsPerformed(
        Hl7.Fhir.Model.Task task)
    {
        int? taskRunCounter = GetTaskRepetitionCount(task);
        if (taskRunCounter is not null && task.Restriction.Repetitions.HasValue &&
            taskRunCounter >= task.Restriction.Repetitions)
        {
            return true;
        }

        return false;
    }

    private void IncrementTaskRepetitionsCounter(
        Hl7.Fhir.Model.Task task)
    {
        List<Hl7.Fhir.Model.Task.OutputComponent> parameterComponent = GetTaskRepetitionCounterOutputComponent(task);

        if (parameterComponent.Count == 0)
        {
            task.Output.Add(new Hl7.Fhir.Model.Task.OutputComponent()
            {
                Type = new CodeableConcept(
                    system: ProviderDirectoryTaskOutputTypeSystem.Uri.OriginalString,
                    code: ProviderDirectoryTaskOutputType.TaskRepetitionCounter.GetCode()),
                Value = new PositiveInt(1)
            });
            return;
        }

        if (parameterComponent.First().Value is PositiveInt positiveInt)
        {
            positiveInt.Value++;
            return;
        }

        logger.LogError("Expected the output parameter with the Code: {Code} to have a Value of type {FhirDataType}",
            ProviderDirectoryTaskOutputType.TaskRepetitionCounter.GetCode(),
            nameof(PositiveInt));
    }

    private int? GetTaskRepetitionCount(
        Hl7.Fhir.Model.Task task)
    {
        List<Hl7.Fhir.Model.Task.OutputComponent> parameterComponent = GetTaskRepetitionCounterOutputComponent(task);

        if (parameterComponent.Count == 0)
        {
            return null;
        }

        if (parameterComponent.First().Value is PositiveInt positiveInt)
        {
            return positiveInt.Value;
        }

        logger.LogError("Expected the output parameter with the Code: {Code} to have a Value of type {FhirDataType}",
            ProviderDirectoryTaskOutputType.TaskRepetitionCounter.GetCode(),
            nameof(PositiveInt));

        return null;
    }

    private static List<Hl7.Fhir.Model.Task.OutputComponent> GetTaskRepetitionCounterOutputComponent(
        Hl7.Fhir.Model.Task task)
    {
        return task.Output.Where(x =>
            HasCode(x.Type, ProviderDirectoryTaskOutputType.TaskRepetitionCounter.GetCode(),
                ProviderDirectoryTaskOutputTypeSystem.Uri)).ToList();
    }

    private static bool HasCode(
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
}