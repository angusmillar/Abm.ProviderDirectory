namespace Abm.PD.Core.Application.MatchingTaskRunner;

/// <summary>
/// The outcome of one MatchingTaskRunner.Run: how many source_resource rows for the task's target
/// CorrelationId were read and deserialised into a Firely POCO, and how many could not be.
/// </summary>
public sealed record MatchingTaskResult(
    int ProcessedCount,
    int FailedCount);
