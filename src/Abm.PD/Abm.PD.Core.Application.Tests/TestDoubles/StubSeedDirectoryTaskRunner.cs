using Abm.PD.Core.Application.SeedDirectoryTaskRunner;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

public sealed class StubSeedDirectoryTaskRunner(List<int> calls) : ISeedDirectoryTaskRunner
{
    public Task<SeedDirectoryTaskResult> Run(
        SeedDirectoryTask seedDirectoryTask,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        calls.Add(seedDirectoryTask.Id);
        return Task.FromResult(new SeedDirectoryTaskResult(ProcessedCount: 0, FailedCount: 0));
    }
}
