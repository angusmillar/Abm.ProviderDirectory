using System.Net;
using Abm.PD.Core.Api.Tests.Fixtures;

namespace Abm.PD.Core.Api.Tests.Health;

public class HealthCheckTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Live_ReturnsOk()
    {
        HttpResponseMessage response = await HttpClient.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_WithDatabaseReachable_ReturnsOk()
    {
        HttpResponseMessage response = await HttpClient.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
