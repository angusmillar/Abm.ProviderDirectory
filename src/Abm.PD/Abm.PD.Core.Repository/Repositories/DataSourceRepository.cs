using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository.Repositories;

public class DataSourceRepository(ProviderDirectoryDbContext dbContext) : IDataSourceRepository
{
    public async Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.DataSource
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<DataSource?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.DataSource
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<DataSource> AddAsync(
        DataSource dataSource,
        CancellationToken cancellationToken)
    {
        dbContext.DataSource.Add(dataSource);
        await dbContext.SaveChangesAsync(cancellationToken);
        return dataSource;
    }

    public async Task<DataSource?> UpdateAsync(
        int id,
        string code,
        string displayName,
        CancellationToken cancellationToken)
    {
        DataSource? dataSource = await dbContext.DataSource
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dataSource is null)
        {
            return null;
        }

        dataSource.Code = code;
        dataSource.DisplayName = displayName;
        await dbContext.SaveChangesAsync(cancellationToken);
        return dataSource;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        DataSource? dataSource = await dbContext.DataSource
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dataSource is null)
        {
            return false;
        }

        dbContext.DataSource.Remove(dataSource);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<DataSource>> SearchAsync(
        string? code,
        string? displayName,
        CancellationToken cancellationToken)
    {
        IQueryable<DataSource> query = dbContext.DataSource.AsNoTracking();
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
