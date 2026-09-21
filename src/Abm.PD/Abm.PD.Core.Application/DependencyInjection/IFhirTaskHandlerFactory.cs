using Abm.PD.Core.Application.FhirTaskDispatcher;
using Abm.PD.Core.Application.SeedProviderDirectoryTask;

namespace Abm.PD.Core.Application.DependencyInjection;

public interface IFhirTaskHandlerFactory
{
    ITaskHandler Get(FhirTaskHandlerType fhirTaskHandlerType);
}