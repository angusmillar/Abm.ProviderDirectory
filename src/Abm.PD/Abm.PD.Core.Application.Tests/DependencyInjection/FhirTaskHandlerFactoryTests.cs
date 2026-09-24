using Abm.PD.Core.Application.DependencyInjection;
using Abm.PD.Core.Application.FhirTaskDispatcher;
using FhirNavigator;
using Microsoft.Extensions.DependencyInjection;
using Task = System.Threading.Tasks.Task;

namespace Abm.PD.Core.Application.Tests.DependencyInjection;

/// <summary>
/// Regression test for a startup registration bug: IFhirTaskHandlerFactory was registered Singleton
/// while every ITaskHandler is AddKeyedScoped. A singleton's constructor captures the root
/// IServiceProvider, not the ambient scope, so FhirTaskHandlerFactory.Get resolved the keyed scoped
/// ITaskHandler from the root provider and threw
/// "Cannot resolve scoped service ... from root provider" whenever ASP.NET Core's scope validation
/// is active (as it is by default in Development). FhirTaskHandlerFactory must live in the same scope
/// as the ITaskHandler it resolves - AddScoped, not AddSingleton.
/// </summary>
public class FhirTaskHandlerFactoryTests
{
    private sealed class FakeTaskHandler : ITaskHandler
    {
        public async Task<TaskHandlerOutcome> Handle(
            Hl7.Fhir.Model.Task task,
            IFhirNavigator fhirNavigator,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            return new TaskHandlerOutcome(TaskStatus: Hl7.Fhir.Model.Task.TaskStatus.Completed, StatusReason: null);
        }
    }

    private static ServiceProvider BuildProvider(
        Action<IServiceCollection> registerFactory)
    {
        ServiceCollection services = new();
        services.AddKeyedScoped<ITaskHandler, FakeTaskHandler>(FhirTaskHandlerType.SeedProviderDirectory);
        registerFactory(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void Get_FactoryRegisteredScoped_ResolvesTheKeyedScopedTaskHandler()
    {
        using ServiceProvider provider = BuildProvider(
            services => services.AddScoped<IFhirTaskHandlerFactory, FhirTaskHandlerFactory>());

        using IServiceScope scope = provider.CreateScope();
        IFhirTaskHandlerFactory factory = scope.ServiceProvider.GetRequiredService<IFhirTaskHandlerFactory>();

        ITaskHandler taskHandler = factory.Get(FhirTaskHandlerType.SeedProviderDirectory);

        Assert.IsType<FakeTaskHandler>(taskHandler);
    }

    [Fact]
    public void Get_FactoryRegisteredSingleton_ThrowsBecauseItCapturedTheRootProvider()
    {
        using ServiceProvider provider = BuildProvider(
            services => services.AddSingleton<IFhirTaskHandlerFactory, FhirTaskHandlerFactory>());

        using IServiceScope scope = provider.CreateScope();
        IFhirTaskHandlerFactory factory = scope.ServiceProvider.GetRequiredService<IFhirTaskHandlerFactory>();

        Assert.Throws<InvalidOperationException>(
            () => factory.Get(FhirTaskHandlerType.SeedProviderDirectory));
    }
}
