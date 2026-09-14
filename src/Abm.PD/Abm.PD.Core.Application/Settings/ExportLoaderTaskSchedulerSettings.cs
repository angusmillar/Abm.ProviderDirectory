using System.ComponentModel.DataAnnotations;

namespace Abm.PD.Core.Application.Settings;

public record ExportLoaderTaskSchedulerSettings
{
    public const string SectionName = "ExportLoaderTaskScheduler";

    /// <summary>
    /// How often the scheduler checks for due tasks. Independent of any task's own TriggerEvery - this
    /// is the poll granularity, not a schedule.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "23:59:59")]
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a task may sit InProgress before the scheduler assumes its runner crashed or was
    /// killed mid-run (no clean Completed/Failed update ever arrived) and reaps it back to Failed.
    /// Must comfortably exceed the slowest real export/load run, or a live task gets reaped out from
    /// under itself.
    /// </summary>
    [Range(typeof(TimeSpan), "00:05:00", "24:00:00")]
    public TimeSpan StaleInProgressAfter { get; init; } = TimeSpan.FromHours(2);
}
