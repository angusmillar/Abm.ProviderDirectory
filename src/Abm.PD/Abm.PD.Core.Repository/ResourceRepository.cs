using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository;

public class ResourceRepository(ProviderDirectoryDbContext dbContext) : IResourceRepository
{
    public async Task<IReadOnlyList<Resource>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Resources
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Resource?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Resources
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<Resource> AddAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync(cancellationToken);
        return resource;
    }

    public async Task<Resource?> UpdateAsync(
        int id,
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken)
    {
        Resource? resource = await dbContext.Resources
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (resource is null)
        {
            return null;
        }

        resource.ResourceType = resourceType;
        resource.ResourceId = resourceId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return resource;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        Resource? resource = await dbContext.Resources
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (resource is null)
        {
            return false;
        }

        dbContext.Resources.Remove(resource);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<Resource>> SearchAsync(
        string? resourceType,
        string? resourceId,
        CancellationToken cancellationToken)
    {
        IQueryable<Resource> query = dbContext.Resources.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            query = query.Where(x => x.ResourceType == resourceType);
        }

        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            query = query.Where(x => x.ResourceId == resourceId);
        }

        return await query.ToListAsync(cancellationToken);
    }
}
