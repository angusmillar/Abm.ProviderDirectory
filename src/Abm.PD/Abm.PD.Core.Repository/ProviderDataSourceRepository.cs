using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository;

public class ProviderDataSourceRepository(ProviderDirectoryDbContext dbContext) : IProviderDataSourceRepository
{
    public async Task<IReadOnlyList<ProviderDataSource>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ProviderDataSource
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<ProviderDataSource?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.ProviderDataSource
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<ProviderDataSource> AddAsync(
        ProviderDataSource providerDataSource,
        CancellationToken cancellationToken)
    {
        dbContext.ProviderDataSource.Add(providerDataSource);
        await dbContext.SaveChangesAsync(cancellationToken);
        return providerDataSource;
    }

    public async Task<ProviderDataSource?> UpdateAsync(
        int id,
        string code,
        string displayName,
        CancellationToken cancellationToken)
    {
        ProviderDataSource? providerDataSource = await dbContext.ProviderDataSource
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (providerDataSource is null)
        {
            return null;
        }

        providerDataSource.Code = code;
        providerDataSource.DisplayName = displayName;
        await dbContext.SaveChangesAsync(cancellationToken);
        return providerDataSource;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        ProviderDataSource? providerDataSource = await dbContext.ProviderDataSource
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (providerDataSource is null)
        {
            return false;
        }

        dbContext.ProviderDataSource.Remove(providerDataSource);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ProviderDataSource>> SearchAsync(
        string? code,
        string? displayName,
        CancellationToken cancellationToken)
    {
        IQueryable<ProviderDataSource> query = dbContext.ProviderDataSource.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(code))
        {
            query = query.Where(x => x.Code == code);
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            query = query.Where(x => x.DisplayName == displayName);
        }

        return await query.ToListAsync(cancellationToken);
    }
}
