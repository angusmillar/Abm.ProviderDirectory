using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository.Repositories;

public class ExportTaskRepository(ProviderDirectoryDbContext dbContext) : IExportTaskRepository
{
    public async Task<IReadOnlyList<ExportTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.ExportTasks
            .Include(x => x.DataSource)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<ExportTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.ExportTasks
            .Include(x => x.DataSource)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<ExportTask> AddAsync(
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        // The caller's DataSource instance usually comes from a no-tracking query (or a different
        // DbContext entirely), so it isn't in this context's change tracker yet. Attaching it first
        // lets EF's identity-resolution convention decide the right state - Added if its key is still
        // the CLR default (a genuinely new DataSource), Unchanged otherwise (an existing one) - rather
        // than Add's graph walk treating every untracked reachable entity as new and attempting to
        // re-insert an already-persisted DataSource, which violates its unique key.
        dbContext.Attach(exportTask.DataSource);
        dbContext.ExportTasks.Add(exportTask);
        await dbContext.SaveChangesAsync(cancellationToken);
        return exportTask;
    }

    public async Task<ExportTask?> UpdateAsync(
        int id,
        ExportTask exportTask,
        CancellationToken cancellationToken)
    {
        ExportTask? existing = await dbContext.ExportTasks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        existing.Code = exportTask.Code;
        existing.DisplayName = exportTask.DisplayName;
        existing.Description = exportTask.Description;
        existing.State = exportTask.State;
        existing.StateReason = exportTask.StateReason;
        existing.TriggerEvery = exportTask.TriggerEvery;
        existing.ToStartAtUtc = exportTask.ToStartAtUtc;
        existing.ToEndAtUtc = exportTask.ToEndAtUtc;
        existing.LastStart = exportTask.LastStart;
        existing.LastEnd = exportTask.LastEnd;
        existing.DataSourceId = exportTask.DataSourceId;
        // CreatedUtc is deliberately never copied here - immutable after insert. UpdatedUtc always
        // is, caller-owned like every other field above.
        existing.UpdatedUtc = exportTask.UpdatedUtc;
        // Mutate the tracked owned instance in place rather than replacing the reference - EF Core's
        // change tracking for owned types is more reliable against property mutation than reassignment.
        existing.Parameter.Type = exportTask.Parameter.Type;
        existing.Parameter.Since = exportTask.Parameter.Since;
        existing.Parameter.TypeFilterList = exportTask.Parameter.TypeFilterList;

        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        ExportTask? existing = await dbContext.ExportTasks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        dbContext.ExportTasks.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ExportTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken)
    {
        IQueryable<ExportTask> query = dbContext.ExportTasks
            .Include(x => x.DataSource)
            .AsNoTracking();

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
