using Abm.PD.Core.Application;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Api.Tests.TestDoubles;

/// <summary>
/// A per-test-configurable IExportRunner substituted for the real one in CoreApiWebApplicationFactory,
/// so ExportLoaderTaskScheduler never makes a real FHIR HTTP call during any Abm.PD.Core.Api.Tests run.
/// Registered as a singleton - tests within the shared IntegrationTestCollection run sequentially, so
/// setting Behaviour per test is safe.
/// </summary>
public sealed class ConfigurableExportRunner : IExportRunner
{
    public Func<ExportLoaderTask, CancellationToken, Task<SourceResourceLoadResult>>? Behaviour { get; set; }

    public Task<SourceResourceLoadResult> Run(
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        if (Behaviour is null)
        {
            throw new InvalidOperationException(
                $"{nameof(ConfigurableExportRunner)}.{nameof(Behaviour)} was not set before the scheduler ran.");
        }

        return Behaviour(exportLoaderTask, cancellationToken);
    }
}
