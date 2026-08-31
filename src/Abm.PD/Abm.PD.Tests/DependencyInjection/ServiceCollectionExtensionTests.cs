using Abm.PD.Domain.DateTimeSupport;
using Abm.PD.Domain.DependencyInjection;
using Abm.PD.Domain.FhirBulkExport;
using Abm.PD.Domain.HttpClientSupport;
using Abm.PD.Domain.Settings;
using FhirNavigator.FhirHttpClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Abm.PD.Tests.DependencyInjection;

/// <summary>
/// The registrations are verified by resolving them, not by inspecting the ServiceCollection, so a missing
/// dependency of FhirBulkExporter shows up here rather than at run time. Nothing here opens a connection: an
/// HttpClient is only configured, never used.
/// </summary>
public class ServiceCollectionExtensionTests
{
    private const string ServiceBaseUrl = "https://provider-directory.invalid.test/fhir";

    private static IConfiguration Configuration(
        Dictionary<string, string?>? overrides = null)
    {
        Dictionary<string, string?> values = new()
        {
            ["ServiceDefaultTimeZone:TimeZoneTimeSpan"] = "10:00",
            ["FhirNavigator:UserAgentName"] = "Abm.PD.Tests",
            ["FhirNavigator:UserAgentVersion"] = "1.0",
            ["FhirNavigator:FhirRepositories:0:Code"] = HttpClientType.ProviderConnectAustralia,
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
        services.AddProviderDirectoryServices(configuration ?? Configuration());
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void AddProviderDirectoryServices_TheBulkExporterResolvesWithAllOfItsDependencies()
    {
        using ServiceProvider serviceProvider = BuildProvider();
        using IServiceScope scope = serviceProvider.CreateScope();

        IFhirBulkExporter exporter = scope.ServiceProvider.GetRequiredService<IFhirBulkExporter>();

        Assert.IsType<FhirBulkExporter>(exporter);
    }

    [Fact]
    public void AddProviderDirectoryServices_TheBulkExporterIsScopedSoOneInstanceIsOneExportSession()
    {
        //The exporter holds the JobId and the manifest in fields, so sharing one across scopes would let two
        //callers trample each other's session.
        using ServiceProvider serviceProvider = BuildProvider();

        using IServiceScope firstScope = serviceProvider.CreateScope();
        using IServiceScope secondScope = serviceProvider.CreateScope();

        IFhirBulkExporter first = firstScope.ServiceProvider.GetRequiredService<IFhirBulkExporter>();
        IFhirBulkExporter alsoFirst = firstScope.ServiceProvider.GetRequiredService<IFhirBulkExporter>();
        IFhirBulkExporter second = secondScope.ServiceProvider.GetRequiredService<IFhirBulkExporter>();

        Assert.Same(first, alsoFirst);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void AddProviderDirectoryServices_TheBulkExporterIsNotResolvableFromTheRootScope()
    {
        using ServiceProvider serviceProvider = BuildProvider();

        Assert.Throws<InvalidOperationException>(
            () => serviceProvider.GetRequiredService<IFhirBulkExporter>());
    }

    [Fact]
    public void AddProviderDirectoryServices_TheDateTimeProviderIsASingleton()
    {
        using ServiceProvider serviceProvider = BuildProvider();

        IDateTimeProvider first = serviceProvider.GetRequiredService<IDateTimeProvider>();
        IDateTimeProvider second = serviceProvider.GetRequiredService<IDateTimeProvider>();

        Assert.IsType<DateTimeProvider>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void AddProviderDirectoryServices_BindsAndValidatesTheServiceTimeZone()
    {
        using ServiceProvider serviceProvider = BuildProvider();

        ServiceDefaultTimeZoneSettings settings =
            serviceProvider.GetRequiredService<IOptions<ServiceDefaultTimeZoneSettings>>().Value;

        Assert.Equal(TimeSpan.FromHours(10), settings.TimeZoneTimeSpan);
    }

    [Fact]
    public void AddProviderDirectoryServices_ATimeZoneOutsideTheAllowedRangeFailsValidation()
    {
        //The annotated range is 00:00 to 23:59, so a negative offset binds but must not pass validation.
        using ServiceProvider serviceProvider = BuildProvider(
            Configuration(new Dictionary<string, string?> { ["ServiceDefaultTimeZone:TimeZoneTimeSpan"] = "-01:00" }));

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => serviceProvider.GetRequiredService<IOptions<ServiceDefaultTimeZoneSettings>>().Value);

        Assert.Contains(nameof(ServiceDefaultTimeZoneSettings.TimeZoneTimeSpan), string.Join("; ", exception.Failures));
    }

    [Fact]
    public void AddProviderDirectoryServices_ATimeZoneThatIsNotATimeSpanFailsToBind()
    {
        using ServiceProvider serviceProvider = BuildProvider(
            Configuration(new Dictionary<string, string?> { ["ServiceDefaultTimeZone:TimeZoneTimeSpan"] = "25:00" }));

        Assert.Throws<InvalidOperationException>(
            () => serviceProvider.GetRequiredService<IOptions<ServiceDefaultTimeZoneSettings>>().Value);
    }

    [Fact]
    public void AddProviderDirectoryServices_RegistersBothClientFactoriesUnderTheRepositoryCode()
    {
        //Both factories are keyed by the repository Code, which has to match the HttpClientType constant the
        //exporter passes in. A mismatch here is the classic configuration failure for this solution.
        using ServiceProvider serviceProvider = BuildProvider();

        HttpClient httpClient = serviceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(HttpClientType.ProviderConnectAustralia);

        Assert.Equal(new Uri(ServiceBaseUrl), httpClient.BaseAddress);

        Hl7.Fhir.Rest.FhirClient fhirClient = serviceProvider
            .GetRequiredService<IFhirHttpClientFactory>()
            .CreateClient(HttpClientType.ProviderConnectAustralia);

        //Firely normalises its endpoint with a trailing slash, the HttpClient's base address is left as configured.
        Assert.Equal(new Uri($"{ServiceBaseUrl}/"), fhirClient.Endpoint);
    }

    [Fact]
    public void AddProviderDirectoryServices_AnUnknownRepositoryCodeHasNoBaseAddressConfigured()
    {
        //IHttpClientFactory hands back a default client for a name it has never been told about, so an
        //unconfigured code fails as a missing BaseAddress rather than as a missing registration.
        using ServiceProvider serviceProvider = BuildProvider();

        HttpClient httpClient = serviceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(HttpClientType.AzurePyroFhirServer);

        Assert.Null(httpClient.BaseAddress);
    }

    [Fact]
    public void AddProviderDirectoryServices_AMissingFhirNavigatorSectionIsAStartUpFailure()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceDefaultTimeZone:TimeZoneTimeSpan"] = "10:00"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();

        Assert.Throws<InvalidOperationException>(
            () => services.AddProviderDirectoryServices(configuration));
    }
}
