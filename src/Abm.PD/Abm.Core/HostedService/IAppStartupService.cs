namespace Abm.Core.HostedService;

public interface IAppStartupService
{
    public Task DoWork(CancellationToken cancellationToken);
}
