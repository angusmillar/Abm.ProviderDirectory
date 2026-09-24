using Abm.PD.BulkExport.DependencyInjection;
using Abm.PD.BulkExport.FhirBulkExport;
using Abm.PD.BulkExport.Loader;
using Abm.PD.BulkExport.Settings;
using Abm.PD.BulkExport.Tests.TestDoubles;
using FhirNavigator.FhirHttpClient;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Abm.Core.Time;

namespace Abm.PD.BulkExport.Tests.DependencyInjection;

/// <summary>
/// The registrations are verified by resolving them, not by inspecting the ServiceCollection, so a missing
/// dependency of FhirBulkExporter shows up here rather than at run time. Nothing here opens a connection: an
/// HttpClient is only configured, never used.
///
/// AddFhirBulkExportServices no longer registers FhirNavigator itself — that moved to
/// Abm.PD.Core.Application.DependencyInjection.ServiceCollectionExtension.AddFhirNavigatorServices, so its own
/// tests cover the FhirNavigator wiring. IFhirHttpClientFactory and IHttpClientFactory are stood in for here
/// instead, the same way a caller composing the real app would have already registered them.
/// </summary>
public class ServiceCollectionExtensionTests
{
    private static IConfiguration Configuration(
        Dictionary<string, string?>? overrides = null)
    {
        Dictionary<string, string?> values = new()
        {
            ["Time:ServiceDefaultTimeZone"] = "10:00"
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

        //Stands in for FhirNavigator's own registrations, which a real composition root adds via
        //AddFhirNavigatorServices before calling AddFhirBulkExportServices.
        services.AddHttpClient();
        services.AddSingleton<IFhirHttpClientFactory>(
            new StubFhirHttpClientFactory(new FhirClient(new Uri(TestUrls.ServiceBaseUrl), new HttpClient())));

        services.AddFhirBulkExportServices(configuration ?? Configuration());
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
    public void AddProviderDirectoryServices_TheBatchLoaderResolvesWithAllOfItsDependencies()
    {
        using ServiceProvider serviceProvider = BuildProvider();
        using IServiceScope scope = serviceProvider.CreateScope();

        IFhirTransactionLoader loader = scope.ServiceProvider.GetRequiredService<IFhirTransactionLoader>();

        Assert.IsType<BulkExport.Loader.FhirTransactionLoader>(loader);
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

        TimeSettings settings =
            serviceProvider.GetRequiredService<IOptions<TimeSettings>>().Value;

        Assert.Equal(TimeSpan.FromHours(10), settings.ServiceDefaultTimeZone);
    }

    [Fact]
    public void AddProviderDirectoryServices_ATimeZoneOutsideTheAllowedRangeFailsValidation()
    {
        //The annotated range is 00:00 to 23:59, so a negative offset binds but must not pass validation.
        using ServiceProvider serviceProvider = BuildProvider(
            Configuration(new Dictionary<string, string?> { ["Time:ServiceDefaultTimeZone"] = "-01:00" }));

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => serviceProvider.GetRequiredService<IOptions<TimeSettings>>().Value);

        Assert.Contains(nameof(TimeSettings.ServiceDefaultTimeZone), string.Join("; ", exception.Failures));
    }

    [Fact]
    public void AddProviderDirectoryServices_ATimeZoneThatIsNotATimeSpanFailsToBind()
    {
        using ServiceProvider serviceProvider = BuildProvider(
            Configuration(new Dictionary<string, string?> { ["Time:ServiceDefaultTimeZone"] = "25:00" }));

        Assert.Throws<InvalidOperationException>(
            () => serviceProvider.GetRequiredService<IOptions<TimeSettings>>().Value);
    }

    [Fact]
    public void AddProviderDirectoryServices_TheStreamedExportHttpClientTimeoutDefaultsToTwoHours()
    {
        using ServiceProvider serviceProvider = BuildProvider();

        FhirBulkExporterSettings settings =
            serviceProvider.GetRequiredService<IOptions<FhirBulkExporterSettings>>().Value;

        Assert.Equal(TimeSpan.FromHours(2), settings.StreamedExportHttpClientTimeout);
    }

    [Fact]
    public void AddProviderDirectoryServices_TheStreamedExportHttpClientTimeoutCanBeOverridden()
    {
        using ServiceProvider serviceProvider = BuildProvider(
            Configuration(new Dictionary<string, string?>
            {
                ["FhirBulkExporter:StreamedExportHttpClientTimeout"] = "01:00:00"
            }));

        FhirBulkExporterSettings settings =
            serviceProvider.GetRequiredService<IOptions<FhirBulkExporterSettings>>().Value;

        Assert.Equal(TimeSpan.FromHours(1), settings.StreamedExportHttpClientTimeout);
    }
}
