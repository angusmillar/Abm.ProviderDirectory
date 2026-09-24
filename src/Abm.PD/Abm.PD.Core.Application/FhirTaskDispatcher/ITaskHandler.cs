using FhirNavigator;

namespace Abm.PD.Core.Application.FhirTaskDispatcher;

public interface ITaskHandler
{
    Task<TaskHandlerOutcome> Handle(
        Hl7.Fhir.Model.Task task, 
        IFhirNavigator fhirNavigator,
        Guid correlationId,
        CancellationToken cancellationToken);
}