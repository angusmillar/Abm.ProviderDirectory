using System.Text.Json.Serialization;
using Abm.PD.Core.Api.Endpoints;
using Abm.PD.Core.Api.Settings;
using Abm.PD.Core.Repository;
using Abm.PD.Core.Repository.DependencyInjection;
using Abm.PD.BulkExport.DependencyInjection;
using Abm.PD.Core.Application.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging, configured entirely from the Serilog config section.
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Every domain enum exposed over the API (TaskStateId, TaskTypeId) is serialised and bound by its
// member name rather than its underlying int, so callers see "InProgress" instead of 2.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOptions<DatabaseSettings>()
    .Bind(builder.Configuration.GetSection(DatabaseSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddCoreRepositoryServices(builder.Configuration);
builder.Services.AddFhirBulkExportServices(builder.Configuration);
builder.Services.AddCoreProviderDirectoryServices(builder.Configuration);

builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    // Resolved inside this factory, not eagerly above: the factory runs when the health check
    // executes, which is after the host has finished building - so it picks up configuration
    // overrides a WebApplicationFactory applies during Build() (see Abm.PD.Core.Api.Tests's
    // CoreApiWebApplicationFactory), where an eager read here would have already captured the
    // pre-override connection string. Same reasoning as AddCoreRepositoryServices's AddDbContext call.
    .AddNpgSql(
        connectionStringFactory: sp =>
        {
            IConfiguration configuration = sp.GetRequiredService<IConfiguration>();
            string? connectionString = configuration.GetConnectionString("ProviderDirectoryDb");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Missing required connection string 'ConnectionStrings:ProviderDirectoryDb'.");
            }

            return connectionString;
        },
        name: "postgres",
        tags: ["ready"]);

var app = builder.Build();

DatabaseSettings databaseSettings = app.Services.GetRequiredService<IOptions<DatabaseSettings>>().Value;

if (databaseSettings.RunMigrationsOnStartup)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    ProviderDirectoryDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapOpenApi();
app.MapScalarApiReference();

app.MapResourceEndpoints();
app.MapDataSourceEndpoints();
app.MapExportTaskEndpoints();
app.MapSeedDirectoryTaskEndpoints();
app.MapHealthCheckEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> in Abm.PD.Core.Api.Tests - top-level statements
// otherwise generate an internal Program class the test assembly cannot reference.
// ReSharper disable once ClassNeverInstantiated.Global
public partial class Program { }
