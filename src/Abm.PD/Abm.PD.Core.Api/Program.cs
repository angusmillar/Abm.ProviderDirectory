using Abm.PD.Core.Api.Settings;
using Abm.PD.Core.Domain.Repositories;
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

builder.Services.AddCoreRepositoryServices(builder.Configuration);

var app = builder.Build();

DatabaseSettings databaseSettings = app.Services.GetRequiredService<IOptions<DatabaseSettings>>().Value;

if (databaseSettings.RunMigrationsOnStartup)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    ProviderDirectoryDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapGet("/resources", async (IResourceRepository resourceRepository, CancellationToken cancellationToken) =>
    await resourceRepository.GetAllAsync(cancellationToken));

app.Run();
