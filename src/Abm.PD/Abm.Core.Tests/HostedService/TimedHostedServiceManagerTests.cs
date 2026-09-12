using Abm.Core.HostedService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Abm.Core.Tests.HostedService;

public class TimedHostedServiceManagerTests
{
    private sealed class SlowTimedService(TimeSpan workDuration) : ITimedHostedService
    {
        private int _concurrentCalls;

        public int CallCount { get; private set; }
        public int MaxObservedConcurrency { get; private set; }

        public async Task DoWork(CancellationToken cancellationToken)
        {
            int concurrent = Interlocked.Increment(ref _concurrentCalls);
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, concurrent);
            CallCount++;
            await Task.Delay(workDuration, cancellationToken);
            Interlocked.Decrement(ref _concurrentCalls);
        }
    }

    [Fact]
    public async Task StartAsync_DoWorkSlowerThanInterval_NeverRunsConcurrentlyWithItself()
    {
        // DoWork (120ms) deliberately outlasts the tick interval (30ms) - this is exactly the
        // "still-running previous execution" scenario the whole scheduler design depends on the
        // tick engine handling correctly, in-process, before any DB-level claim is added on top.
        SlowTimedService slowService = new(TimeSpan.FromMilliseconds(120));
        ServiceCollection services = new();
        services.AddSingleton(slowService);
        await using ServiceProvider provider = services.BuildServiceProvider();

        TimedHostedServiceManagerOptions<SlowTimedService> options = new()
        {
            TriggersEvery = TimeSpan.FromMilliseconds(30),
        };
        TimedHostedServiceManager<SlowTimedService> manager = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TimedHostedServiceManager<SlowTimedService>>.Instance,
            options);

        await manager.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        await manager.StopAsync(CancellationToken.None);

        Assert.Equal(1, slowService.MaxObservedConcurrency);
        Assert.True(slowService.CallCount >= 2, $"Expected at least 2 ticks, got {slowService.CallCount}");
    }
}
