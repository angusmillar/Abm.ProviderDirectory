using Abm.PD.Core.Application.FhirTaskDispatcher;
using FhirNavigator;

namespace Abm.PD.Core.Application.SeedProviderDirectoryTask;

public class SeedProviderDirectoryTaskHandler : ITaskHandler
{
    public async Task Handle(
        Hl7.Fhir.Model.Task task, 
        IFhirNavigator fhirNavigator, 
        CancellationToken cancellationToken)
    {
        
    }
}