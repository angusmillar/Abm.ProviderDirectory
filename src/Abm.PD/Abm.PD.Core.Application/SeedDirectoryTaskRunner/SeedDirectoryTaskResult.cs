namespace Abm.PD.Core.Application.SeedDirectoryTaskRunner;

/// <summary>
/// The outcome of one SeedDirectoryTaskRunner.Run: how many source_resource rows for the task's target
/// CorrelationId were read and deserialised into a Firely POCO, and how many could not be.
/// </summary>
public sealed record SeedDirectoryTaskResult(
    int ProcessedCount,
    int FailedCount);
