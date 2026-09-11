using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Domain.Repositories;

public interface IResourceRepository
{
    Task<IReadOnlyList<Resource>> GetAllAsync(CancellationToken cancellationToken);

    Task<Resource?> GetByIdAsync(
        string resourceId,
        CancellationToken cancellationToken);

    Task<Resource> AddAsync(
        Resource resource,
        CancellationToken cancellationToken);

    Task<Resource?> UpdateAsync(
        string resourceId,
        string resourceType,
        string newResourceId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        string resourceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Resource>> SearchAsync(
        string? resourceType,
        string? resourceId,
        CancellationToken cancellationToken);
}
