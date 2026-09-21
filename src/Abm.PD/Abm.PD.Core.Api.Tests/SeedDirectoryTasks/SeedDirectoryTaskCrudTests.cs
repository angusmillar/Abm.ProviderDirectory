using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Tests.SeedDirectoryTasks;

public class SeedDirectoryTaskCrudTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    // The API serialises TaskStateId/TaskTypeId as their member name (see Program.cs's
    // ConfigureHttpJsonOptions), so responses containing those enums need the matching converter to
    // deserialise here - System.Net.Http.Json otherwise uses JsonSerializerOptions.Default, which only
    // understands the underlying int.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static SeedDirectoryTaskRequest NewRequest(
        string code,
        TaskStateId state = TaskStateId.Ready,
        DateTimeOffset? toStartAtUtc = null)
    {
        return new SeedDirectoryTaskRequest(
            Code: code,
            DisplayName: $"Task {code}",
            Description: "Nightly matching run",
            State: state,
            StateReason: null,
            TriggerEvery: TimeSpan.FromHours(24),
            StartAtUtc: toStartAtUtc,
            EndAtUtc: null,
            MaxRunCount: null,
            MetaData: "{}");
    }

    private static SeedDirectoryTaskUpdateRequest AsUpdateRequest(SeedDirectoryTaskResponse response) => new(
        DisplayName: response.DisplayName,
        Description: response.Description,
        State: response.State,
        StateReason: response.StateReason,
        TriggerEvery: response.TriggerEvery,
        StartAtUtc: response.StartAtUtc,
        EndAtUtc: response.EndAtUtc,
        MaxRunCount: response.MaxRunCount,
        MetaData: response.MetaData);

    [Fact]
    public async Task Create_ValidRequest_Returns201WithCreatedSeedDirectoryTask()
    {
        SeedDirectoryTaskRequest request = NewRequest(Guid.NewGuid().ToString());

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/SeedDirectoryTask", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        SeedDirectoryTaskResponse? created = await response.Content.ReadFromJsonAsync<SeedDirectoryTaskResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(request.Code, created!.Code);
        Assert.Equal(request.DisplayName, created.DisplayName);
        Assert.Equal(TaskTypeId.SeedDirectoryTask, created.TypeId);
        Assert.Null(created.LastStartUtc);
        Assert.Null(created.LastEndUtc);
        Assert.NotEqual(default, created.CreatedUtc);
        Assert.NotEqual(default, created.UpdatedUtc);
        Assert.Equal(0, created.FailureCount);
        Assert.Equal(0, created.RunCount);
        Assert.Null(created.MaxRunCount);
        Assert.Null(created.LastCorrelationId);
    }

    [Fact]
    public async Task Create_ValidRequest_SerialisesTypeIdAndStateAsEnumNamesNotIntegers()
    {
        SeedDirectoryTaskRequest request = NewRequest(Guid.NewGuid().ToString());

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/SeedDirectoryTask", request);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"typeId\":\"SeedDirectoryTask\"", body);
        Assert.Contains("\"state\":\"Ready\"", body);
        Assert.DoesNotContain("\"typeId\":2", body);
        Assert.DoesNotContain("\"state\":1", body);
    }

    [Fact]
    public async Task Create_DuplicateCode_Returns400()
    {
        string code = Guid.NewGuid().ToString();
        HttpResponseMessage firstResponse = await HttpClient.PostAsJsonAsync("/SeedDirectoryTask", NewRequest(code));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/SeedDirectoryTask", NewRequest(code));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ExistingSeedDirectoryTask_ReturnsSeedDirectoryTask()
    {
        SeedDirectoryTaskRequest request = NewRequest(Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/SeedDirectoryTask", request);
        SeedDirectoryTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<SeedDirectoryTaskResponse>(JsonOptions))!;

        SeedDirectoryTaskResponse? fetched =
            await HttpClient.GetFromJsonAsync<SeedDirectoryTaskResponse>($"/SeedDirectoryTask/{created.Id}", JsonOptions);

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(request.Code, fetched.Code);
    }

    [Fact]
    public async Task GetById_NonExistentSeedDirectoryTask_Returns404()
    {
        HttpResponseMessage response = await HttpClient.GetAsync("/SeedDirectoryTask/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AfterCreate_ContainsCreatedSeedDirectoryTask()
    {
        SeedDirectoryTaskRequest request = NewRequest(Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/SeedDirectoryTask", request);
        SeedDirectoryTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<SeedDirectoryTaskResponse>(JsonOptions))!;

        List<SeedDirectoryTaskResponse>? all =
            await HttpClient.GetFromJsonAsync<List<SeedDirectoryTaskResponse>>("/SeedDirectoryTask", JsonOptions);

        Assert.NotNull(all);
        Assert.Contains(all!, x => x.Id == created.Id);
    }

    [Fact]
    public async Task Search_ByCode_FindsMatchingSeedDirectoryTask()
    {
        string code = Guid.NewGuid().ToString();
        await HttpClient.PostAsJsonAsync("/SeedDirectoryTask", NewRequest(code));

        List<SeedDirectoryTaskResponse>? results = await HttpClient.GetFromJsonAsync<List<SeedDirectoryTaskResponse>>(
            $"/SeedDirectoryTask?code={code}", JsonOptions);

        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal(code, results![0].Code);
    }

    [Fact]
    public async Task Update_ExistingSeedDirectoryTask_PersistsChangesAndPreservesLastStart()
    {
        SeedDirectoryTaskRequest request = NewRequest(Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/SeedDirectoryTask", request);
        SeedDirectoryTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<SeedDirectoryTaskResponse>(JsonOptions))!;
        // Postgres timestamptz truncates to microsecond precision, so the in-memory CreatedUtc from
        // the POST response has finer resolution than what a DB round-trip will return - fetch the
        // persisted baseline rather than comparing against it directly.
        SeedDirectoryTaskResponse persistedBaseline =
            (await HttpClient.GetFromJsonAsync<SeedDirectoryTaskResponse>($"/SeedDirectoryTask/{created.Id}", JsonOptions))!;

        SeedDirectoryTaskUpdateRequest updateRequest = AsUpdateRequest(created) with
        {
            DisplayName = "Updated Display Name",
            State = TaskStateId.InProgress,
            StateReason = "Running now",
        };
        HttpResponseMessage updateResponse = await HttpClient.PutAsJsonAsync($"/SeedDirectoryTask/{created.Id}", updateRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        SeedDirectoryTaskResponse? updated = await updateResponse.Content.ReadFromJsonAsync<SeedDirectoryTaskResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Updated Display Name", updated!.DisplayName);
        Assert.Equal(TaskStateId.InProgress, updated.State);
        Assert.Equal(persistedBaseline.CreatedUtc, updated.CreatedUtc);
        Assert.Null(updated.LastStartUtc);
        Assert.True(updated.UpdatedUtc >= persistedBaseline.UpdatedUtc);
        // Code is not part of the update payload - must survive unchanged.
        Assert.Equal(created.Code, updated.Code);
    }

    [Fact]
    public async Task Update_NonExistentSeedDirectoryTask_Returns404()
    {
        SeedDirectoryTaskRequest request = NewRequest(Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/SeedDirectoryTask", request);
        SeedDirectoryTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<SeedDirectoryTaskResponse>(JsonOptions))!;
        SeedDirectoryTaskUpdateRequest updateRequest = AsUpdateRequest(created);

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync("/SeedDirectoryTask/999999", updateRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingSeedDirectoryTask_Returns204ThenGetByIdReturns404()
    {
        SeedDirectoryTaskRequest request = NewRequest(Guid.NewGuid().ToString());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/SeedDirectoryTask", request);
        SeedDirectoryTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<SeedDirectoryTaskResponse>(JsonOptions))!;

        HttpResponseMessage deleteResponse = await HttpClient.DeleteAsync($"/SeedDirectoryTask/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"/SeedDirectoryTask/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_NonExistentSeedDirectoryTask_Returns404()
    {
        HttpResponseMessage response = await HttpClient.DeleteAsync("/SeedDirectoryTask/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
