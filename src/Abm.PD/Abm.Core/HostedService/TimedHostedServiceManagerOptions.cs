namespace Abm.Core.HostedService;

public class TimedHostedServiceManagerOptions<T> where T : ITimedHostedService
{
    public TimeSpan TriggersEvery { get; set; } = TimeSpan.FromSeconds(30);
}
