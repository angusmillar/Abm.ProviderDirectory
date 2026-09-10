using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
            });
        });
    }
}
