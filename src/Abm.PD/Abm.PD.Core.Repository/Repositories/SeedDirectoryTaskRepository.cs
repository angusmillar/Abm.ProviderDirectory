using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository.Repositories;

public class SeedDirectoryTaskRepository(ProviderDirectoryDbContext dbContext) : ISeedDirectoryTaskRepository
{
    public async Task<IReadOnlyList<SeedDirectoryTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.SeedDirectoryTasks
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<SeedDirectoryTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.SeedDirectoryTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<SeedDirectoryTask> AddAsync(
        SeedDirectoryTask seedDirectoryTask,
        CancellationToken cancellationToken)
    {
        dbContext.SeedDirectoryTasks.Add(seedDirectoryTask);
        await dbContext.SaveChangesAsync(cancellationToken);
        return seedDirectoryTask;
    }

    public async Task<SeedDirectoryTask?> UpdateAsync(
        int id,
        SeedDirectoryTask seedDirectoryTask,
        CancellationToken cancellationToken)
    {
        SeedDirectoryTask? existing = await dbContext.SeedDirectoryTasks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        existing.Code = seedDirectoryTask.Code;
        existing.DisplayName = seedDirectoryTask.DisplayName;
        existing.Description = seedDirectoryTask.Description;
        existing.State = seedDirectoryTask.State;
        existing.StateReason = seedDirectoryTask.StateReason;
        existing.TriggerEvery = seedDirectoryTask.TriggerEvery;
        existing.StartAtUtc = seedDirectoryTask.StartAtUtc;
        existing.EndAtUtc = seedDirectoryTask.EndAtUtc;
        existing.MaxRunCount = seedDirectoryTask.MaxRunCount;
        existing.LastStartUtc = seedDirectoryTask.LastStartUtc;
        existing.LastEndUtc = seedDirectoryTask.LastEndUtc;
        existing.MetaData = seedDirectoryTask.MetaData;
        // CreatedUtc is deliberately never copied here - immutable after insert. UpdatedUtc always
        // is, caller-owned like every other field above.
        existing.UpdatedUtc = seedDirectoryTask.UpdatedUtc;

        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        SeedDirectoryTask? existing = await dbContext.SeedDirectoryTasks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        dbContext.SeedDirectoryTasks.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<SeedDirectoryTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken)
    {
        IQueryable<SeedDirectoryTask> query = dbContext.SeedDirectoryTasks
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
            query = query.Where(x => x.LastStartUtc != null && x.LastStartUtc >= lastStartFrom);
        }

        if (lastStartTo is not null)
        {
            query = query.Where(x => x.LastStartUtc != null && x.LastStartUtc <= lastStartTo);
        }

        return await query.ToListAsync(cancellationToken);
    }
}
