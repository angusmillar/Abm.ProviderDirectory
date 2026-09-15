using Abm.PD.Core.Application;
using Abm.PD.Core.Application.ExportTaskRunner;
using Abm.PD.Core.Application.Loader;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Api.Tests.TestDoubles;

/// <summary>
/// A per-test-configurable IExportRunner substituted for the real one in CoreApiWebApplicationFactory,
/// so TaskScheduler never makes a real FHIR HTTP call during any Abm.PD.Core.Api.Tests run.
/// Registered as a singleton - tests within the shared IntegrationTestCollection run sequentially, so
/// setting Behaviour per test is safe.
/// </summary>
public sealed class ConfigurableExportTaskRunner : IExportTaskRunner
{
    public Func<ExportTask, Guid, CancellationToken, Task<SourceResourceLoadResult>>? Behaviour { get; set; }

    public Task<SourceResourceLoadResult> Run(
        ExportTask exportTask,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (Behaviour is null)
        {
            throw new InvalidOperationException(
                $"{nameof(ConfigurableExportTaskRunner)}.{nameof(Behaviour)} was not set before the scheduler ran.");
        }

        return Behaviour(exportTask, correlationId, cancellationToken);
    }
}
