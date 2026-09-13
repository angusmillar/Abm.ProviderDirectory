using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Application.Tests.Loader.TestDoubles;

public sealed class FakeSourceResourceRepository : ISourceResourceRepository
{
    public List<IReadOnlyCollection<SourceResource>> ReceivedBatches { get; } = [];

    public Func<IReadOnlyCollection<SourceResource>, CancellationToken, Task>? OnAddRangeAsync { get; set; }

    public async Task AddRangeAsync(
        IReadOnlyCollection<SourceResource> sourceResources,
        CancellationToken cancellationToken)
    {
        ReceivedBatches.Add(sourceResources);

        if (OnAddRangeAsync is not null)
        {
            await OnAddRangeAsync(sourceResources, cancellationToken);
        }
    }
}
