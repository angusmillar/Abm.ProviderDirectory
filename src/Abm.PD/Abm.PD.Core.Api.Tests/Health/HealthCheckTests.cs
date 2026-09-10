using System.Net;
using System.Text.Json;
using Abm.PD.Core.Api.Tests.Fixtures;

namespace Abm.PD.Core.Api.Tests.Health;

public class HealthCheckTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Live_ReturnsOk()
    {
        HttpResponseMessage response = await HttpClient.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        // Liveness runs no downstream checks by design - pin that contract here.
        Assert.Equal(0, body.RootElement.GetProperty("entries").GetPropertyCount());
    }

    [Fact]
    public async Task Ready_WithDatabaseReachable_ReturnsOk()
    {
        HttpResponseMessage response = await HttpClient.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "Healthy",
            body.RootElement.GetProperty("entries").GetProperty("postgres").GetProperty("status").GetString());
    }
}
