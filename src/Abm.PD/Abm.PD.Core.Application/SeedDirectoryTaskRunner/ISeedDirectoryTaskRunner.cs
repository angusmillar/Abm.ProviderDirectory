using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Application.SeedDirectoryTaskRunner;

public interface ISeedDirectoryTaskRunner
{
    Task<SeedDirectoryTaskResult> Run(
        SeedDirectoryTask seedDirectoryTask,
        Guid correlationId,
        CancellationToken cancellationToken);
}
