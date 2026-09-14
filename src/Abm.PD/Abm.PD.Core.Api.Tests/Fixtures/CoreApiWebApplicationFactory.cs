using Abm.PD.Core.Api.Tests.TestDoubles;
using Abm.PD.Core.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Abm.PD.Core.Api.Tests.Fixtures;

public class CoreApiWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Point the app at the Testcontainers Postgres container instead of local dev Postgres.
                ["ConnectionStrings:ProviderDirectoryDb"] = connectionString,

                // IntegrationTestFixture migrates the container directly before this factory starts;
                // the app must not also try to migrate on every WebApplicationFactory boot.
                ["Database:RunMigrationsOnStartup"] = "false",

                // Quiet logging in tests.
                ["Serilog:MinimumLevel:Default"] = "Warning",

                // The longest PollInterval TaskSchedulerSettings' own validation allows
                // (00:10:00) - still vastly longer than any test run, so the scheduler's own background
                // timer never ticks during a test run. IntegrationTestFixture builds one factory for the
                // whole test collection's lifetime, so without this the real 30-second production default
                // would keep firing in the background across every other test in the suite.
                ["TaskScheduler:PollInterval"] = "00:10:00",
                // The minimum this repo's settings validation allows - short enough that a reaped-task
                // test only needs a LastStart a few minutes in the past, not the 2-hour production default.
                ["TaskScheduler:StaleInProgressAfter"] = "00:05:00",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Swaps the real ExportRunner (which would otherwise make real FHIR HTTP calls) for a
            // per-test-configurable fake, for the lifetime of this factory.
            services.RemoveAll<IExportRunner>();
            services.AddSingleton<ConfigurableExportRunner>();
            services.AddSingleton<IExportRunner>(sp => sp.GetRequiredService<ConfigurableExportRunner>());
        });
    }
}
