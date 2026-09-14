using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Application.Tests.TestDoubles;

// TaskScheduler re-fetches the fully-loaded ExportTask by Id after claiming it (see the design
// spec's "why the claimed instance can't be used directly" callout) - GetByIdAsync must actually
// work for that flow to be exercised in these tests, unlike the other CRUD members, which nothing
// here calls.
public sealed class InMemoryExportTaskRepository(List<ExportTask> tasks) : IExportTaskRepository
{
    public Task<IReadOnlyList<ExportTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ExportTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(tasks.SingleOrDefault(t => t.Id == id));
    }

    public Task<ExportTask> AddAsync(
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<ExportTask?> UpdateAsync(
        int id,
        ExportTask exportTask,
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

    public Task<IReadOnlyList<ExportTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
