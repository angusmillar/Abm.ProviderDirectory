using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Entities;

/// <summary>
/// A job definition is an entity that describes one particular type of executable work that could be run as a Job
/// </summary>
public abstract class TaskBase
{
    protected TaskBase(TaskTypeId typeId)
    {
        TypeId = typeId;
    }

    public int Id { get; set; }

    // Set only by each subtype's constructor - a real, ValueGenerated.Never CLR property is not
    // otherwise protected by EF from being handed a mismatched value at construction time, which
    // would silently write a row that is unreachable through its own subtype's DbSet while still
    // occupying its slot in the unique Code index. EF Core materialises via reflection and can still
    // set this through the private setter.
    public TaskTypeId TypeId { get; private set; }

    public required string Code { get; set; }

    public required string DisplayName { get; set; }

    public string? Description { get; set; }

    public required TaskStateId State { get; set; }

    public required string? StateReason { get; set; }

    public required TimeSpan TriggerEvery { get; set; }

    public required DateTime? StartAtUtc { get; set; }

    public required DateTime? EndAtUtc { get; set; }

    public required DateTime CreatedUtc { get; set; }

    public required DateTime UpdatedUtc { get; set; }

    public required DateTime? LastStartUtc { get; set; }

    public required DateTime? LastEndUtc { get; set; }

    // Counts consecutive failures since the last Completed run - reset to zero on success. Compared
    // against TaskSchedulerSettings.FailureAttemptCount to decide whether a Failed task
    // is still Due.
    public int FailureCount { get; set; }

    // Total number of times this task has been run by a TaskRunner.
    public int RunCount { get; set; }

    // Maximum number of runs permitted before the task is considered finished (null = run indefinitely).
    public int? MaxRunCount { get; set; }

    // A version 7 GUID minted fresh by the TaskScheduler for each run and persisted here by
    // TryClaimAsync as part of the claim itself - our own identifier for correlating that run's logs
    // and its persisted SourceResource rows, independent of whatever job id the target system (e.g.
    // the FHIR bulk export server) assigns.
    public Guid? LastCorrelationId { get; set; }
}
