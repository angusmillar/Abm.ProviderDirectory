using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Entities;

/// <summary>
/// Lookup/reference row for a TaskStateId enum member. Seeded once per enum value; not
/// referentially enforced against consuming columns - see TaskBase.State.
/// </summary>
public class TaskState
{
    private TaskState()
    {
    }

    public TaskState(TaskStateId taskStateId, string name)
    {
        TaskStateId = taskStateId;
        Name = name;
    }

    public TaskStateId TaskStateId { get; set; }

    public string Name { get; set; } = null!;
}
