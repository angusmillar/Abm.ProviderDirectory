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
}
