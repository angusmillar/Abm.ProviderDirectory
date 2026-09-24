using Abm.PD.Core.Application.FhirTaskDispatcher;
using Abm.PD.Core.Application.SeedProviderDirectoryTask;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Application.DependencyInjection;

public interface IFhirTaskHandlerFactory
{
    ITaskHandler Get(FhirTaskHandlerType fhirTaskHandlerType);
}