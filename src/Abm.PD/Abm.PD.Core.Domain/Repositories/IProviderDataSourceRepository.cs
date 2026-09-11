using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Domain.Repositories;

public interface IProviderDataSourceRepository
{
    Task<IReadOnlyList<ProviderDataSource>> GetAllAsync(CancellationToken cancellationToken);

    Task<ProviderDataSource?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<ProviderDataSource> AddAsync(
        ProviderDataSource providerDataSource,
        CancellationToken cancellationToken);

    Task<ProviderDataSource?> UpdateAsync(
        int id,
        string code,
        string displayName,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderDataSource>> SearchAsync(
        string? code,
        string? displayName,
        CancellationToken cancellationToken);
}
