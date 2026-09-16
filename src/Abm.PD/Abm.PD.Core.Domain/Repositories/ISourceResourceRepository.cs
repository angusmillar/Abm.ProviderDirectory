using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Domain.Repositories;

public interface ISourceResourceRepository
{
    Task AddRangeAsync(
        IReadOnlyCollection<SourceResource> sourceResources,
        CancellationToken cancellationToken);

    // Yields one tracked SourceResource at a time so a caller can mutate and SaveChangesAsync
    // per item as it iterates. Never holds more than one resource's JSON payload in memory - only
    // the (lightweight) matching Ids are materialised up front.
    IAsyncEnumerable<SourceResource> GetByCorrelationIdAsync(
        Guid correlationId,
        string resourceType,
        CancellationToken cancellationToken);
}
