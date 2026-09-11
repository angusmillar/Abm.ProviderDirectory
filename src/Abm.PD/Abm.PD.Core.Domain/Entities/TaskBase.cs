using Abm.PD.Core.Domain.Enums;
using TaskStatus = System.Threading.Tasks.TaskStatus;

namespace Abm.PD.Core.Domain.Entities;

/// <summary>
/// A job definition is an entity that describes one particular type of executable work that could be run as a Job  
/// </summary>
public abstract class TaskBase
{
    public int Id { get; set; }
    
    public abstract TaskTypeId TypeId { get; }
    
    public abstract required string Code { get; set; }
    
    public abstract required string DisplayName { get; set; }
    
    public abstract string? Description { get; set; }
    
    public required TaskStatus Status { get; set; }
    
    public required string? StatusReason { get; set; }
    
    public required TimeSpan TriggerEvery { get; set; }
    
    public required DateTime? ToStartAtUtc { get; set; }
    
    public required DateTime? ToEndAtUtc { get; set; }
    
    public required DateTime CreatedUtc { get; set; }
    
    public required DateTime UpdatedUtc { get; set; }
    
    public required DateTime? LastStart { get; set; }
    
    public required DateTime? LastEnd { get; set; }
    
}