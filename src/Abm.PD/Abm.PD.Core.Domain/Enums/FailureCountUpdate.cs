namespace Abm.PD.Core.Domain.Enums;

/// <summary>
/// How IExportLoaderTaskRepository.RecordOutcomeAsync should treat TaskBase.FailureCount for the
/// outcome being recorded.
/// </summary>
public enum FailureCountUpdate
{
    Unchanged = 0,
    Reset = 1,
    Increment = 2
}
