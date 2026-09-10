namespace Abm.PD.Core.Api.Tests.Fixtures;

[Collection(nameof(IntegrationTestCollection))]
public abstract class IntegrationTestBase(IntegrationTestFixture fixture) : IAsyncLifetime
{
    protected HttpClient HttpClient => fixture.HttpClient;

    // Runs before every [Fact] - xUnit creates a fresh instance of the derived test class per test
    // method, so every test starts from a genuinely empty database.
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
