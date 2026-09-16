using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Abm.PD.Core.Repository.Repositories;

public class MatchingTaskRepository(ProviderDirectoryDbContext dbContext) : IMatchingTaskRepository
{
    public async Task<IReadOnlyList<MatchingTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.MatchingTasks
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<MatchingTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.MatchingTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<MatchingTask> AddAsync(
        MatchingTask matchingTask,
        CancellationToken cancellationToken)
    {
        dbContext.MatchingTasks.Add(matchingTask);
        await dbContext.SaveChangesAsync(cancellationToken);
        return matchingTask;
    }

    public async Task<MatchingTask?> UpdateAsync(
        int id,
        MatchingTask matchingTask,
        CancellationToken cancellationToken)
    {
        MatchingTask? existing = await dbContext.MatchingTasks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        existing.Code = matchingTask.Code;
        existing.DisplayName = matchingTask.DisplayName;
        existing.Description = matchingTask.Description;
        existing.State = matchingTask.State;
        existing.StateReason = matchingTask.StateReason;
        existing.TriggerEvery = matchingTask.TriggerEvery;
        existing.StartAtUtc = matchingTask.StartAtUtc;
        existing.EndAtUtc = matchingTask.EndAtUtc;
        existing.MaxRunCount = matchingTask.MaxRunCount;
        existing.LastStartUtc = matchingTask.LastStartUtc;
        existing.LastEndUtc = matchingTask.LastEndUtc;
        existing.MetaData = matchingTask.MetaData;
        // CreatedUtc is deliberately never copied here - immutable after insert. UpdatedUtc always
        // is, caller-owned like every other field above.
        existing.UpdatedUtc = matchingTask.UpdatedUtc;

        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        MatchingTask? existing = await dbContext.MatchingTasks
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        dbContext.MatchingTasks.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<MatchingTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken)
    {
        IQueryable<MatchingTask> query = dbContext.MatchingTasks
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
