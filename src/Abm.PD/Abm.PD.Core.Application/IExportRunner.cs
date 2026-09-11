namespace Abm.PD.Core.Application;

public interface IExportRunner
{
    Task Run(CancellationToken cancellationToken);
}