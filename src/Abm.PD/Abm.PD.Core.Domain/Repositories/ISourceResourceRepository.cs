using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Projections;

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

    // Builds the whole correlation's source-to-target reference map in memory: SeedDirectoryTaskRunner needs
    // random access by "{ResourceType}/{ResourceId}" while it rewrites every reference on every
    // resource, so unlike GetByCorrelationIdAsync this cannot be streamed one row at a time. Every entry's
    // AssignedTargetResourceId comes back null - SourceResource carries no such column - it is left for the
    // caller to populate, see SourceToTargetResourceIdLookup's doc comment.
    Task<Dictionary<string, SourceToTargetResourceIdLookup>> GetSourceToTargetResourceIdDictionaryAsync(
        Guid correlationId,
        CancellationToken cancellationToken);

    // UpdatedUtc is set to the moment of the call, not caller-supplied, so SeedDirectoryTaskRunner doesn't
    // need a timestamp source of its own for what is otherwise a single-property write. Returns false
    // rather than throwing when id doesn't match a row, mirroring TaskRepository.TryClaimAsync.
    Task<bool> UpdateResourceAsync(
        int id,
        string resource,
        CancellationToken cancellationToken); 
}
