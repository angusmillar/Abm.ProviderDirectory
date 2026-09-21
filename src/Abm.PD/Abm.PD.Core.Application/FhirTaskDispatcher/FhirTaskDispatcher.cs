using Abm.Core.HostedService;
using Abm.Core.Time;
using Abm.PD.Core.Application.DependencyInjection;
using Abm.PD.Core.Application.Settings;
using FhirNavigator;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.FhirTaskDispatcher;

public class FhirTaskDispatcher(
    ILogger<FhirTaskDispatcher> logger,
    IFhirNavigatorFactory fhirNavigatorFactory,
    IOptions<ProviderDirectorySettings> providerDirectorySettings,
    IDateTimeProvider dateTimeProvider,
    IFhirTaskHandlerFactory fhirTaskHandlerFactory
    ) : ITimedHostedService
{
    
    private readonly Uri FhirTaskCodeSystemsUri = new Uri("http://corus-ix.au.fhir.telstrahealth.com/ImplementationGuide/CodeSystem/providerDirectory-task-code");
    private readonly Dictionary<string, FhirTaskHandlerType> FhirTaskCodeTypeMap = new()
    {
        { "seed-provider-directory-resources", FhirTaskHandlerType.SeedProviderDirectory }
    };
    
    public async Task DoWork(
        CancellationToken cancellationToken)
    {
        string[] repositoryCodeList =
        [
            providerDirectorySettings.Value.FhirRepositoryCodeAssignment.TelstraHealthProviderDirectoryTarget,
            providerDirectorySettings.Value.FhirRepositoryCodeAssignment.HealthConnectProviderDirectorySource,
            providerDirectorySettings.Value.FhirRepositoryCodeAssignment.HealthLinkProviderDirectorySource
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
        groupSearchParams.Add("period", $"ge{dateTimeProvider.Now.ToFhirDateTime()}");
        groupSearchParams.Add("_count", "100");

        IFhirNavigator fhirNavigator = fhirNavigatorFactory.GetFhirNavigator(repositoryCode);

        try
        {
            SearchInfo searchInfo = await fhirNavigator.Search<Hl7.Fhir.Model.Task>(groupSearchParams);
        
            logger.LogInformation("Dispatching {ResourceTotal} FHIR Tasks from the FHIR server {RepositoryCode}:{RepositoryDisplay} ", 
                fhirNavigator.RepositorySettings.Code,
                fhirNavigator.RepositorySettings.DisplayName,
                searchInfo.ResourceTotal);
            
            var taskList = fhirNavigator.Cache.GetList<Hl7.Fhir.Model.Task>();
            fhirNavigator.Cache.Clear();
            
            foreach (Hl7.Fhir.Model.Task task in taskList)
            {
                await DispatchFhirTask(task, fhirNavigator, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
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
        if (fhirTaskHandlerType.HasValue)
        {
            ITaskHandler taskHandler = fhirTaskHandlerFactory.Get(fhirTaskHandlerType.Value);
            try
            {
                await taskHandler.Handle(task, fhirNavigator, cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error processing FHIR Task/{ResourceId} from the FHIR Server {RepositoryCode}:{RepositoryDisplay} at {Endpoint}",
                    task.Id,
                    fhirNavigator.RepositorySettings.Code,
                    fhirNavigator.RepositorySettings.DisplayName,
                    fhirNavigator.RepositorySettings.ServiceBaseUrl.OriginalString);
            }
        }
    }
    
    private FhirTaskHandlerType? GetTaskType(
        CodeableConcept? codeableConcept)
    {
        
        var codeList = codeableConcept?.Coding.Where(x => 
            x.System.Equals(FhirTaskCodeSystemsUri.OriginalString,  StringComparison.OrdinalIgnoreCase)).ToList();
        
        foreach (KeyValuePair<string, FhirTaskHandlerType> fhirTaskHandlerType in FhirTaskCodeTypeMap)
        {
            if (codeList?.FirstOrDefault(x => x.Code.Equals(fhirTaskHandlerType.Key, StringComparison.OrdinalIgnoreCase)) != null)
            {
                return fhirTaskHandlerType.Value;
            }
        }
        
        return null;
    }
}