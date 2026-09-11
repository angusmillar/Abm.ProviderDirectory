using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository;

public class ExportLoaderTaskRepository(ProviderDirectoryDbContext dbContext) : IExportLoaderTaskRepository
{
    public async Task<IReadOnlyList<ExportLoaderTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ExportLoaderTasks
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<ExportLoaderTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.ExportLoaderTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<ExportLoaderTask> AddAsync(
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        dbContext.ExportLoaderTasks.Add(exportLoaderTask);
        await dbContext.SaveChangesAsync(cancellationToken);
        return exportLoaderTask;
    }

    public async Task<ExportLoaderTask?> UpdateAsync(
        int id,
        ExportLoaderTask exportLoaderTask,
        CancellationToken cancellationToken)
    {
        ExportLoaderTask? existing = await dbContext.ExportLoaderTasks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        existing.Code = exportLoaderTask.Code;
        existing.DisplayName = exportLoaderTask.DisplayName;
        existing.Description = exportLoaderTask.Description;
        existing.State = exportLoaderTask.State;
        existing.StateReason = exportLoaderTask.StateReason;
        existing.TriggerEvery = exportLoaderTask.TriggerEvery;
        existing.ToStartAtUtc = exportLoaderTask.ToStartAtUtc;
        existing.ToEndAtUtc = exportLoaderTask.ToEndAtUtc;
        existing.LastStart = exportLoaderTask.LastStart;
        existing.LastEnd = exportLoaderTask.LastEnd;
        // CreatedUtc is deliberately never copied here - immutable after insert. UpdatedUtc always
        // is, caller-owned like every other field above.
        existing.UpdatedUtc = exportLoaderTask.UpdatedUtc;
        // Mutate the tracked owned instance in place rather than replacing the reference - EF Core's
        // change tracking for owned types is more reliable against property mutation than reassignment.
        existing.Parameter.Type = exportLoaderTask.Parameter.Type;
        existing.Parameter.Since = exportLoaderTask.Parameter.Since;
        existing.Parameter.TypeFilterList = exportLoaderTask.Parameter.TypeFilterList;

        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        ExportLoaderTask? existing = await dbContext.ExportLoaderTasks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        dbContext.ExportLoaderTasks.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ExportLoaderTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken)
    {
        IQueryable<ExportLoaderTask> query = dbContext.ExportLoaderTasks.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(code))
        {
            query = query.Where(x => x.Code == code);
        }

        if (state is not null)
        {
            query = query.Where(x => x.State == state);
        }

        if (lastStartFrom is not null)
        {
            query = query.Where(x => x.LastStart != null && x.LastStart >= lastStartFrom);
        }

        if (lastStartTo is not null)
        {
            query = query.Where(x => x.LastStart != null && x.LastStart <= lastStartTo);
        }

        return await query.ToListAsync(cancellationToken);
    }
}
