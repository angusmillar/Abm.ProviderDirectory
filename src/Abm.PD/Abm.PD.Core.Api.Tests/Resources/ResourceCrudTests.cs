using System.Net;
using System.Net.Http.Json;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;

namespace Abm.PD.Core.Api.Tests.Resources;

public class ResourceCrudTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Create_ValidRequest_Returns201WithCreatedResource()
    {
        ResourceRequest request = new("Practitioner", Guid.NewGuid().ToString());

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/Resource", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Resource? created = await response.Content.ReadFromJsonAsync<Resource>();
        Assert.NotNull(created);
        Assert.Equal(request.ResourceType, created!.ResourceType);
        Assert.Equal(request.ResourceId, created.ResourceId);
    }

    [Fact]
    public async Task GetById_ExistingResource_ReturnsMatchingResource()
    {
        ResourceRequest request = new("Organization", Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/Resource", request);
        Resource created = (await createResponse.Content.ReadFromJsonAsync<Resource>())!;

        Resource? fetched = await HttpClient.GetFromJsonAsync<Resource>($"/Resource/{created.ResourceId}");

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(request.ResourceType, fetched.ResourceType);
    }

    [Fact]
    public async Task GetAll_AfterCreate_ContainsCreatedResource()
    {
        ResourceRequest request = new("Location", Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/Resource", request);
        Resource created = (await createResponse.Content.ReadFromJsonAsync<Resource>())!;

        List<Resource>? all = await HttpClient.GetFromJsonAsync<List<Resource>>("/Resource");

        Assert.NotNull(all);
        Assert.Contains(all!, r => r.Id == created.Id);
    }

    [Fact]
    public async Task Search_ByResourceTypeAndId_FindsMatchingResource()
    {
        string resourceId = Guid.NewGuid().ToString();
        ResourceRequest request = new("Endpoint", resourceId);
        await HttpClient.PostAsJsonAsync("/Resource", request);

        List<Resource>? results = await HttpClient.GetFromJsonAsync<List<Resource>>(
            $"/Resource?type=Endpoint&id={resourceId}");

        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal(resourceId, results![0].ResourceId);
    }

    [Fact]
    public async Task Update_ExistingResource_PersistsChanges()
    {
        ResourceRequest request = new("HealthcareService", Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/Resource", request);
        Resource created = (await createResponse.Content.ReadFromJsonAsync<Resource>())!;

        // GetById now addresses by ResourceId, so the update keeps it stable and changes ResourceType instead.
        ResourceRequest updateRequest = new("PractitionerRole", created.ResourceId);
        HttpResponseMessage updateResponse = await HttpClient.PutAsJsonAsync($"/Resource/{created.ResourceId}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Resource? updated = await updateResponse.Content.ReadFromJsonAsync<Resource>();
        Assert.Equal(updateRequest.ResourceType, updated!.ResourceType);

        // The PUT response reflects the tracked in-memory entity - fetch it back to prove the
        // change actually persisted to Postgres.
        Resource? fetched = await HttpClient.GetFromJsonAsync<Resource>($"/Resource/{created.ResourceId}");
        Assert.NotNull(fetched);
        Assert.Equal(updateRequest.ResourceType, fetched!.ResourceType);
    }

    [Fact]
    public async Task Delete_ExistingResource_Returns204ThenGetByIdReturns404()
    {
        ResourceRequest request = new("PractitionerRole", Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/Resource", request);
        Resource created = (await createResponse.Content.ReadFromJsonAsync<Resource>())!;

        HttpResponseMessage deleteResponse = await HttpClient.DeleteAsync($"/Resource/{created.ResourceId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"/Resource/{created.ResourceId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentResource_Returns404()
    {
        ResourceRequest updateRequest = new("Practitioner", Guid.NewGuid().ToString());

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync($"/Resource/{Guid.NewGuid()}", updateRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
