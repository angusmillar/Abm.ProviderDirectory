using Abm.Core.Enums;
using Abm.PD.Core.Application.ExportTaskRunner;
using Abm.PD.Core.Application.FhirTaskDispatcher;
using Hl7.Fhir.Model;
using Task = Hl7.Fhir.Model.Task;

namespace Abm.PD.Console;

public static class FhirTaskFactory
{
    
    public static Task CreateFhirTask()
    {
        return new Task()
        {
            Code = new CodeableConcept(
                system: FhirTaskHandlerTypeSystem.Uri.OriginalString, 
                code: FhirTaskHandlerType.SeedProviderDirectory.GetCode()),
            Description =
                "This task performs the initial seeding of FHIR resources into the target Provider Directory " +
                "FHIR server. It is intended to be run only once, during initialisation.",
            Status = Task.TaskStatus.OnHold,
            Intent = Task.TaskIntent.Plan,
            ExecutionPeriod = null,
            AuthoredOnElement = new FhirDateTime(DateTimeOffset.Now),
            LastModifiedElement = new FhirDateTime(DateTimeOffset.Now),
            Restriction = new Task.RestrictionComponent()
            {
                Repetitions = 1,
                Period = new Period()
                {
                    StartElement = new FhirDateTime(DateTimeOffset.Now),
                    EndElement = new FhirDateTime(DateTimeOffset.Now.Add(TimeSpan.FromHours(24))),
                },
            },
            Input = new List<Task.ParameterComponent>()
            {
                new Task.ParameterComponent()
                {
                    Type = new CodeableConcept()
                    {
                        Coding = new List<Coding>()
                        {
                            new Coding()
                            {
                                Code = ProviderDirectoryTaskInputType.FhirBulkExportRequestParameters.GetCode(),
                                System = ProviderDirectoryTaskInputTypeSystem.Uri.OriginalString
                            }
                        },
                    },
                    Value = new ResourceReference()
                    {
                        Reference = "#Parameters"
                    }
                }
            },
            Contained = new List<Resource>()
            {
                FhirExportQuery.GetEveryThingFrom(
                    fromDateTime: DateTimeOffset.Now.Subtract(TimeSpan.FromDays(365.25 * 5)), 
                    resourceId: "#Parameters")
            }
        };
    }
}