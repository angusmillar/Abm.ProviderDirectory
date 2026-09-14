using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// ExportLoaderTaskScheduler re-fetches the fully-loaded ExportLoaderTask by Id after claiming it (see
// the design spec's "why the claimed instance can't be used directly" callout) - GetByIdAsync must
// actually work for that flow to be exercised in these tests, unlike the other CRUD members, which
// nothing here calls.
public sealed class InMemoryExportLoaderTaskRepository(List<ExportLoaderTask> tasks) : IExportLoaderTaskRepository
{
    public Task<IReadOnlyList<ExportLoaderTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ExportLoaderTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(tasks.SingleOrDefault(t => t.Id == id));
    }

    public Task<ExportLoaderTask> AddAsync(
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ExportLoaderTask?> UpdateAsync(
        int id,
        ExportLoaderTask exportLoaderTask,
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

    public Task<IReadOnlyList<ExportLoaderTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
