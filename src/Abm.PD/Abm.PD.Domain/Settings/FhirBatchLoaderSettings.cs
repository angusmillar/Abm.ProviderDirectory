using System.ComponentModel.DataAnnotations;

namespace Abm.PD.Domain.Settings;

public record FhirBatchLoaderSettings
{
    public const string SectionName = "FhirBatchLoader";

    /// <summary>
    /// How many resources are gathered before a batch is committed. The target server caps both the number of
    /// bundle entries and the size of a request body, so this is tuned against that server rather than guessed.
    /// </summary>
    [Range(1, 10000)] public int BatchSize { get; init; } = 500;

    /// <summary>
    /// How many failed resources are retained for reporting. Every failure is counted and logged, but only this
    /// many are kept, so a load that fails on every resource still holds bounded memory.
    /// </summary>
    [Range(0, 10000)] public int MaxRetainedFailures { get; init; } = 100;
}
