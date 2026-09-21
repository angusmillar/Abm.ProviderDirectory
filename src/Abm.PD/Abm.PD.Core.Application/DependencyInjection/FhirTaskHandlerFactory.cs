using Abm.PD.Core.Application.FhirTaskDispatcher;
using Abm.PD.Core.Application.SeedProviderDirectoryTask;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Application.DependencyInjection;

public class FhirTaskHandlerFactory(IServiceProvider serviceProvider) : IFhirTaskHandlerFactory
{
    public ITaskHandler Get(FhirTaskHandlerType fhirTaskHandlerType)
    {
        return serviceProvider.GetRequiredKeyedService<ITaskHandler>(fhirTaskHandlerType);
    }
    
}
