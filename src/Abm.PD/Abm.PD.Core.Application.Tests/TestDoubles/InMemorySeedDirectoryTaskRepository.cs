using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// TaskScheduler re-fetches the fully-loaded SeedDirectoryTask by Id after claiming it, mirroring
// InMemoryExportTaskRepository - GetByIdAsync must actually work for that flow to be exercised in
// these tests, unlike the other CRUD members, which nothing here calls.
public sealed class InMemorySeedDirectoryTaskRepository(List<SeedDirectoryTask> tasks) : ISeedDirectoryTaskRepository
{
    public Task<IReadOnlyList<SeedDirectoryTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<SeedDirectoryTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(tasks.SingleOrDefault(t => t.Id == id));
    }

    public Task<SeedDirectoryTask> AddAsync(
        SeedDirectoryTask seedDirectoryTask,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<SeedDirectoryTask?> UpdateAsync(
        int id,
        SeedDirectoryTask seedDirectoryTask,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<SeedDirectoryTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
