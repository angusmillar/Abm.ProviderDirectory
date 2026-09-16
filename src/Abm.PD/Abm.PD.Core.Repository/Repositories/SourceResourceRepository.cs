using System.Runtime.CompilerServices;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository.Repositories;

public class SourceResourceRepository(ProviderDirectoryDbContext dbContext) : ISourceResourceRepository
{
    public async Task AddRangeAsync(
        IReadOnlyCollection<SourceResource> sourceResources,
        CancellationToken cancellationToken)
    {
        // The caller's DataSource instance usually comes from a different DbContext (or a batch
        // reuses one instance across many resources), so it isn't in this context's change tracker
        // yet. Attaching it first lets EF's identity-resolution convention decide the right state -
        // Unchanged for an already-persisted DataSource - rather than AddRange's graph walk treating
        // every untracked reachable entity as new and attempting to re-insert it. DistinctBy(Id)
        // rather than Distinct(), since two calls into this method (one per batch) may pass different
        // DataSource instances carrying the same key.
        foreach (DataSource dataSource in sourceResources.Select(x => x.DataSource).DistinctBy(x => x.Id))
        {
            if (dbContext.Entry(dataSource).State == EntityState.Detached)
            {
                dbContext.Attach(dataSource);
            }
        }

        dbContext.SourceResources.AddRange(sourceResources);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async IAsyncEnumerable<SourceResource> GetByCorrelationIdAsync(
        Guid correlationId,
        string resourceType,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Ids only, materialised up front: cheap even for a large correlation, and it leaves the
        // dbContext free of an open reader so the caller can mutate and SaveChangesAsync on it
        // between yields - a query held open across the whole enumeration would collide with that
        // per-item write ("a second operation was started on this context before the previous one
        // completed").
        List<int> ids = await dbContext.SourceResources
            .Where(x => x.CorrelationId == correlationId && x.ResourceType == resourceType)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (int id in ids)
        {
            yield return await dbContext.SourceResources.SingleAsync(x => x.Id == id, cancellationToken);
        }
    }
}
