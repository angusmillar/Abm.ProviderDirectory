using System.Net;
using System.Net.Http.Json;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class ExportLoaderTaskCrudTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<int> CreateDataSourceIdAsync()
    {
        DataSourceRequest request = new(Guid.NewGuid().ToString(), "Provider Connect Australia");
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/DataSource", request);
        DataSource created = (await response.Content.ReadFromJsonAsync<DataSource>())!;
        return created.Id;
    }

    private static ExportLoaderTaskRequest NewRequest(
        string code,
        int dataSourceId,
        TaskStateId state = TaskStateId.Ready,
        DateTime? toStartAtUtc = null)
    {
        return new ExportLoaderTaskRequest(
            Code: code,
            DisplayName: $"Task {code}",
            Description: "Nightly bulk import",
            State: state,
            StateReason: null,
            TriggerEvery: TimeSpan.FromHours(24),
            ToStartAtUtc: toStartAtUtc,
            ToEndAtUtc: null,
            DataSourceId: dataSourceId,
            Parameter: new ExportLoaderTaskParameterRequest(
                Type: "Patient",
                Since: null,
                TypeFilterList: ["Patient"]));
    }

    [Fact]
    public async Task Create_ValidRequest_Returns201WithCreatedExportLoaderTask()
    {
        int dataSourceId = await CreateDataSourceIdAsync();
        ExportLoaderTaskRequest request = NewRequest(Guid.NewGuid().ToString(), dataSourceId);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/ExportLoaderTask", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ExportLoaderTask? created = await response.Content.ReadFromJsonAsync<ExportLoaderTask>();
        Assert.NotNull(created);
        Assert.Equal(request.Code, created!.Code);
        Assert.Equal(request.DisplayName, created.DisplayName);
        Assert.Equal(TaskTypeId.BulkImport, created.TypeId);
        Assert.Equal(new[] { "Patient" }, created.Parameter.TypeFilterList);
        Assert.Equal(dataSourceId, created.DataSourceId);
        Assert.Null(created.LastStart);
        Assert.Null(created.LastEnd);
        Assert.NotEqual(default, created.CreatedUtc);
        Assert.NotEqual(default, created.UpdatedUtc);
    }

    [Fact]
    public async Task Create_NonExistentDataSourceId_Returns400()
    {
        ExportLoaderTaskRequest request = NewRequest(Guid.NewGuid().ToString(), 999999);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/ExportLoaderTask", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ExistingExportLoaderTask_ReturnsMatchingExportLoaderTask()
    {
        int dataSourceId = await CreateDataSourceIdAsync();
        ExportLoaderTaskRequest request = NewRequest(Guid.NewGuid().ToString(), dataSourceId);
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportLoaderTask", request);
        ExportLoaderTask created = (await createResponse.Content.ReadFromJsonAsync<ExportLoaderTask>())!;

        ExportLoaderTask? fetched = await HttpClient.GetFromJsonAsync<ExportLoaderTask>($"/ExportLoaderTask/{created.Id}");

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(request.Code, fetched.Code);
    }

    [Fact]
    public async Task GetById_NonExistentExportLoaderTask_Returns404()
    {
        HttpResponseMessage response = await HttpClient.GetAsync("/ExportLoaderTask/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AfterCreate_ContainsCreatedExportLoaderTask()
    {
        ExportLoaderTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceIdAsync());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportLoaderTask", request);
        ExportLoaderTask created = (await createResponse.Content.ReadFromJsonAsync<ExportLoaderTask>())!;

        List<ExportLoaderTask>? all = await HttpClient.GetFromJsonAsync<List<ExportLoaderTask>>("/ExportLoaderTask");

        Assert.NotNull(all);
        Assert.Contains(all!, x => x.Id == created.Id);
    }

    [Fact]
    public async Task Search_ByCode_FindsMatchingExportLoaderTask()
    {
        string code = Guid.NewGuid().ToString();
        await HttpClient.PostAsJsonAsync("/ExportLoaderTask", NewRequest(code, await CreateDataSourceIdAsync()));

        List<ExportLoaderTask>? results = await HttpClient.GetFromJsonAsync<List<ExportLoaderTask>>(
            $"/ExportLoaderTask?code={code}");

        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal(code, results![0].Code);
    }

    [Fact]
    public async Task Search_ByState_FindsOnlyMatchingState()
    {
        int dataSourceId = await CreateDataSourceIdAsync();
        await HttpClient.PostAsJsonAsync("/ExportLoaderTask", NewRequest(Guid.NewGuid().ToString(), dataSourceId, TaskStateId.Ready));
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync(
            "/ExportLoaderTask", NewRequest(Guid.NewGuid().ToString(), dataSourceId, TaskStateId.InProgress));
        ExportLoaderTask inProgress = (await createResponse.Content.ReadFromJsonAsync<ExportLoaderTask>())!;

        List<ExportLoaderTask>? results = await HttpClient.GetFromJsonAsync<List<ExportLoaderTask>>(
            "/ExportLoaderTask?state=InProgress");

        Assert.NotNull(results);
        Assert.Contains(results!, x => x.Id == inProgress.Id);
        Assert.All(results!, x => Assert.Equal(TaskStateId.InProgress, x.State));
    }

    [Fact]
    public async Task Update_ExistingExportLoaderTask_PersistsChangesAndPreservesLastStart()
    {
        ExportLoaderTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceIdAsync());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportLoaderTask", request);
        ExportLoaderTask created = (await createResponse.Content.ReadFromJsonAsync<ExportLoaderTask>())!;
        // Postgres timestamptz truncates to microsecond precision, so the in-memory CreatedUtc from
        // the POST response has finer resolution than what a DB round-trip will return - fetch the
        // persisted baseline rather than comparing against it directly.
        ExportLoaderTask persistedBaseline = (await HttpClient.GetFromJsonAsync<ExportLoaderTask>($"/ExportLoaderTask/{created.Id}"))!;

        ExportLoaderTaskRequest updateRequest = new(
            Code: created.Code,
            DisplayName: "Updated Display Name",
            Description: created.Description,
            State: TaskStateId.InProgress,
            StateReason: "Running now",
            TriggerEvery: created.TriggerEvery,
            ToStartAtUtc: created.ToStartAtUtc,
            ToEndAtUtc: created.ToEndAtUtc,
            DataSourceId: created.DataSourceId,
            Parameter: new ExportLoaderTaskParameterRequest(
                Type: "Patient,Organization",
                Since: null,
                TypeFilterList: ["Patient", "Organization"]));
        HttpResponseMessage updateResponse = await HttpClient.PutAsJsonAsync($"/ExportLoaderTask/{created.Id}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        ExportLoaderTask? updated = await updateResponse.Content.ReadFromJsonAsync<ExportLoaderTask>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Display Name", updated!.DisplayName);
        Assert.Equal(TaskStateId.InProgress, updated.State);
        Assert.Equal(new[] { "Patient", "Organization" }, updated.Parameter.TypeFilterList);
        Assert.Equal(persistedBaseline.CreatedUtc, updated.CreatedUtc);
        Assert.Null(updated.LastStart);
        Assert.True(updated.UpdatedUtc >= persistedBaseline.UpdatedUtc);

        // The PUT response reflects the tracked in-memory entity - fetch it back to prove the
        // change actually persisted to Postgres.
        ExportLoaderTask? fetched = await HttpClient.GetFromJsonAsync<ExportLoaderTask>($"/ExportLoaderTask/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal("Updated Display Name", fetched!.DisplayName);
        Assert.Equal(new[] { "Patient", "Organization" }, fetched.Parameter.TypeFilterList);
    }

    [Fact]
    public async Task Update_NonExistentExportLoaderTask_Returns404()
    {
        ExportLoaderTaskRequest updateRequest = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceIdAsync());

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync("/ExportLoaderTask/999999", updateRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingExportLoaderTask_Returns204ThenGetByIdReturns404()
    {
        ExportLoaderTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceIdAsync());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportLoaderTask", request);
        ExportLoaderTask created = (await createResponse.Content.ReadFromJsonAsync<ExportLoaderTask>())!;

        HttpResponseMessage deleteResponse = await HttpClient.DeleteAsync($"/ExportLoaderTask/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"/ExportLoaderTask/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_NonExistentExportLoaderTask_Returns404()
    {
        HttpResponseMessage response = await HttpClient.DeleteAsync("/ExportLoaderTask/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
