using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository.Repositories;

public class ResourceRepository(ProviderDirectoryDbContext dbContext) : IResourceRepository
{
    public async Task<IReadOnlyList<Resource>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Resources
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Resource?> GetByIdAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Resources
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ResourceId == resourceId, cancellationToken);
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
        string resourceId,
        string resourceType,
        string newResourceId,
        CancellationToken cancellationToken)
    {
        Resource? resource = await dbContext.Resources
            .FirstOrDefaultAsync(x => x.ResourceId == resourceId, cancellationToken);
        if (resource is null)
        {
            return null;
        }

        resource.ResourceType = resourceType;
        resource.ResourceId = newResourceId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return resource;
    }

    public async Task<bool> DeleteAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        Resource? resource = await dbContext.Resources
            .FirstOrDefaultAsync(x => x.ResourceId == resourceId, cancellationToken);
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
