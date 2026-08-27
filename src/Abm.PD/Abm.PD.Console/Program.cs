using Abm.PD.Console.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

Console.Write("Running: Abm.PD.Console");
var builder = Host.CreateApplicationBuilder(args);

//Use Serilog for logging — clear default providers (console, debug) to prevent duplicate output
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger(), dispose: true);

//Add the global applications Configuration
builder.Services.AddOptions<ConsoleApplicationSettings>()
    .Bind(builder.Configuration.GetSection(ConsoleApplicationSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Add services/tools as required.
//builder.Services.AddScoped<BinaryTesting>();

//Build the host and resolve Application via a scope
using var host = builder.Build();

//Create a new scope
await using var scope = host.Services.CreateAsyncScope();

//Choose the tool to be run:
//await scope.ServiceProvider.GetRequiredService<BinaryTesting>().Run();

