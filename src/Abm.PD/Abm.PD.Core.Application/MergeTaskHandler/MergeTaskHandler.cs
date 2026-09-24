using Abm.PD.Core.Application.FhirTaskDispatcher;
using FhirNavigator;

namespace Abm.PD.Core.Application.MergeTaskHandler;

public class MergeTaskHandler : ITaskHandler
{
    public Task<TaskHandlerOutcome> Handle(
        Hl7.Fhir.Model.Task task,
        IFhirNavigator fhirNavigator,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}