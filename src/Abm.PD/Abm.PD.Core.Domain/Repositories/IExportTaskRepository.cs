using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Domain.Repositories;

public interface IExportTaskRepository
{
    Task<IReadOnlyList<ExportTask>> GetAllAsync(CancellationToken cancellationToken);

    Task<ExportTask?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<ExportTask> AddAsync(
        ExportTask exportTask,
        CancellationToken cancellationToken);

    Task<ExportTask?> UpdateAsync(
        int id,
        ExportTask exportTask,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExportTask>> SearchAsync(
        string? code,
        TaskStateId? state,
        DateTime? lastStartFrom,
        DateTime? lastStartTo,
        CancellationToken cancellationToken);
}
