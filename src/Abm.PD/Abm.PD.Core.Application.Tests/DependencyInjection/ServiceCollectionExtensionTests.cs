using Abm.PD.Core.Application.DependencyInjection;
using FhirNavigator.FhirHttpClient;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Application.Tests.DependencyInjection;

/// <summary>
/// Covers AddFhirNavigatorServices, kept separate from AddCoreProviderDirectoryServices so it can be resolved on
/// its own — Abm.PD.Console calls only this one, not the TaskScheduler/ExportTaskRunner/SeedDirectoryTaskRunner
/// machinery AddCoreProviderDirectoryServices also registers.
///
/// Nothing here opens a connection: an HttpClient is only configured, never used.
/// </summary>
public class ServiceCollectionExtensionTests
{
    private const string ServiceBaseUrl = "https://provider-directory.invalid.test/fhir";
    private const string RepositoryCode = "ProviderConnectAustralia";

    private static IConfiguration Configuration(
        Dictionary<string, string?>? overrides = null)
    {
        Dictionary<string, string?> values = new()
        {
            ["FhirNavigator:UserAgentName"] = "Abm.PD.Core.Application.Tests",
            ["FhirNavigator:UserAgentVersion"] = "1.0",
            ["FhirNavigator:FhirRepositories:0:Code"] = RepositoryCode,
            ["FhirNavigator:FhirRepositories:0:DisplayName"] = "Provider Connect Australia",
            ["FhirNavigator:FhirRepositories:0:ServiceBaseUrl"] = ServiceBaseUrl,
            ["FhirNavigator:FhirRepositories:0:UseOAuth2"] = "false",
            ["FhirNavigator:FhirRepositories:0:UseBasicAuth"] = "false",
            ["FhirNavigator:FhirRepositories:0:UseBearerToken"] = "false"
        };

        if (overrides is not null)
        {
            foreach (KeyValuePair<string, string?> setting in overrides)
            {
                values[setting.Key] = setting.Value;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceProvider BuildProvider(
        IConfiguration? configuration = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddFhirNavigatorServices(configuration ?? Configuration());
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void AddFhirNavigatorServices_RegistersBothClientFactoriesUnderTheRepositoryCode()
    {
        using ServiceProvider serviceProvider = BuildProvider();

        HttpClient httpClient = serviceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(RepositoryCode);

        Assert.Equal(new Uri(ServiceBaseUrl), httpClient.BaseAddress);

        FhirClient fhirClient = serviceProvider
            .GetRequiredService<IFhirHttpClientFactory>()
            .CreateClient(RepositoryCode);

        //Firely normalises its endpoint with a trailing slash, the HttpClient's base address is left as configured.
        Assert.Equal(new Uri($"{ServiceBaseUrl}/"), fhirClient.Endpoint);
    }

    [Fact]
    public void AddFhirNavigatorServices_AnUnknownRepositoryCodeHasNoBaseAddressConfigured()
    {
        //IHttpClientFactory hands back a default client for a name it has never been told about, so an
        //unconfigured code fails as a missing BaseAddress rather than as a missing registration.
        using ServiceProvider serviceProvider = BuildProvider();

        HttpClient httpClient = serviceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("SomeOtherRepositoryCode");

        Assert.Null(httpClient.BaseAddress);
    }

    [Fact]
    public void AddFhirNavigatorServices_AMissingFhirNavigatorSectionIsAStartUpFailure()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        ServiceCollection services = new();
        services.AddLogging();

        Assert.Throws<InvalidOperationException>(
            () => services.AddFhirNavigatorServices(configuration));
    }
}
