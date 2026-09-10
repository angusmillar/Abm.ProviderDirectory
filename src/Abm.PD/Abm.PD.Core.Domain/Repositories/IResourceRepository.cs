using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Domain.Repositories;

public interface IResourceRepository
{
    Task<IReadOnlyList<Resource>> GetAllAsync(CancellationToken cancellationToken);

    Task<Resource?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<Resource> AddAsync(
        Resource resource,
        CancellationToken cancellationToken);

    Task<Resource?> UpdateAsync(
        int id,
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Resource>> SearchAsync(
        string? resourceType,
        string? resourceId,
        CancellationToken cancellationToken);
}
