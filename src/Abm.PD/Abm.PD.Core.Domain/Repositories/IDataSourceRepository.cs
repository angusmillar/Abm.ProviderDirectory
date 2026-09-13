using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Domain.Repositories;

public interface IDataSourceRepository
{
    Task<IReadOnlyList<DataSource>> GetAllAsync(CancellationToken cancellationToken);

    Task<DataSource?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<DataSource> AddAsync(
        DataSource dataSource,
        CancellationToken cancellationToken);

    Task<DataSource?> UpdateAsync(
        int id,
        string code,
        string displayName,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DataSource>> SearchAsync(
        string? code,
        string? displayName,
        CancellationToken cancellationToken);
}
