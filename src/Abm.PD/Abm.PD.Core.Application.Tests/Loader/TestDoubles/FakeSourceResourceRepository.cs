using System.Runtime.CompilerServices;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Projections;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Application.Tests.Loader.TestDoubles;

public sealed class FakeSourceResourceRepository : ISourceResourceRepository
{
    public List<IReadOnlyCollection<SourceResource>> ReceivedBatches { get; } = [];

    public Func<IReadOnlyCollection<SourceResource>, CancellationToken, Task>? OnAddRangeAsync { get; set; }

    public List<SourceResource> SeededResources { get; } = [];

    public List<(Guid CorrelationId, string ResourceType)> ReceivedGetByCorrelationIdCalls { get; } = [];

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

    public async IAsyncEnumerable<SourceResource> GetByCorrelationIdAsync(
        Guid correlationId,
        string resourceType,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ReceivedGetByCorrelationIdCalls.Add((correlationId, resourceType));

        foreach (SourceResource resource in SeededResources.Where(
                     x => x.CorrelationId == correlationId && x.ResourceType == resourceType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return resource;
        }
    }

    public Task<Dictionary<string, SourceToTargetResourceIdLookup>> GetSourceToTargetResourceIdDictionaryAsync(
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        Dictionary<string, SourceToTargetResourceIdLookup> lookup = SeededResources
            .Where(x => x.CorrelationId == correlationId)
            .ToDictionary(
                x => $"{x.ResourceType}/{x.ResourceId}",
                x => new SourceToTargetResourceIdLookup
                {
                    Id = x.Id,
                    ResourceType = x.ResourceType,
                    ResourceId = x.ResourceId,
                });

        return Task.FromResult(lookup);
    }

    public Task<bool> UpdateResourceAsync(
        int id,
        string resource,
        CancellationToken cancellationToken)
    {
        SourceResource? existing = SeededResources.SingleOrDefault(x => x.Id == id);
        if (existing is null)
        {
            return Task.FromResult(false);
        }

        existing.Resource = resource;
        existing.UpdatedUtc = DateTime.UtcNow;
        return Task.FromResult(true);
    }
}
