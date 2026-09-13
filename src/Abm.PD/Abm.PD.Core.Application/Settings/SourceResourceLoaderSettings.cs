using System.ComponentModel.DataAnnotations;

namespace Abm.PD.Core.Application.Settings;

public record SourceResourceLoaderSettings
{
    public const string SectionName = "SourceResourceLoader";

    /// <summary>
    /// How many resources are gathered before a batch is persisted as one SaveChanges call.
    /// </summary>
    [Range(1, 10000)] public int BatchSize { get; init; } = 500;

    /// <summary>
    /// How many failed resources are retained for reporting. Every failure is counted and logged, but only this
    /// many are kept, so a load that fails on every resource still holds bounded memory.
    /// </summary>
    [Range(0, 10000)] public int MaxRetainedFailures { get; init; } = 100;
}
