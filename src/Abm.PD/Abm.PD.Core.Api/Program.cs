using Abm.PD.Core.Api.Endpoints;
using Abm.PD.Core.Api.Settings;
using Abm.PD.Core.Repository;
using Abm.PD.Core.Repository.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging, configured entirely from the Serilog config section.
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddOptions<DatabaseSettings>()
    .Bind(builder.Configuration.GetSection(DatabaseSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

string connectionString = builder.Configuration.GetConnectionString("ProviderDirectoryDb")
    ?? throw new InvalidOperationException(
        "Missing required connection string 'ConnectionStrings:ProviderDirectoryDb'.");

builder.Services.AddCoreRepositoryServices(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

var app = builder.Build();

DatabaseSettings databaseSettings = app.Services.GetRequiredService<IOptions<DatabaseSettings>>().Value;

if (databaseSettings.RunMigrationsOnStartup)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    ProviderDirectoryDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapResourceEndpoints();
app.MapHealthCheckEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> in Abm.PD.Core.Api.Tests - top-level statements
// otherwise generate an internal Program class the test assembly cannot reference.
public partial class Program { }
