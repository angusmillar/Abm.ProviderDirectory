namespace Abm.PD.Core.Api.Tests.Fixtures;

[Collection(nameof(IntegrationTestCollection))]
public abstract class IntegrationTestBase(IntegrationTestFixture fixture) : IAsyncLifetime
{
    // By the time any test runs, InitializeAsync has completed successfully, so this is genuinely
    // non-null here even though the fixture exposes it as nullable to stay disposal-safe.
    protected HttpClient HttpClient => fixture.HttpClient!;

    // Runs before every [Fact] - xUnit creates a fresh instance of the derived test class per test
    // method, so every test starts from a genuinely empty database.
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
