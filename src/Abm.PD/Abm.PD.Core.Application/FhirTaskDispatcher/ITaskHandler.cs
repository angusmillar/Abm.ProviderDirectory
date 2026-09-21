using FhirNavigator;

namespace Abm.PD.Core.Application.FhirTaskDispatcher;

public interface ITaskHandler
{
    Task Handle(
        Hl7.Fhir.Model.Task task, 
        IFhirNavigator fhirNavigator, 
        CancellationToken cancellationToken);
}