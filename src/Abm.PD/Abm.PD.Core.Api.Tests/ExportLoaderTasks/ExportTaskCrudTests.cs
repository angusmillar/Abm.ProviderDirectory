using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Enums;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class ExportTaskCrudTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    // The API serialises TaskStateId/TaskTypeId as their member name (see Program.cs's
    // ConfigureHttpJsonOptions), so responses containing those enums need the matching converter to
    // deserialise here - System.Net.Http.Json otherwise uses JsonSerializerOptions.Default, which only
    // understands the underlying int.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<string> CreateDataSourceCodeAsync()
    {
        string code = Guid.NewGuid().ToString();
        DataSourceRequest request = new(code, "Provider Connect Australia");
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/DataSource", request);
        response.EnsureSuccessStatusCode();
        return code;
    }

    private static ExportTaskRequest NewRequest(
        string code,
        string dataSourceCode,
        TaskStateId state = TaskStateId.Ready,
        DateTimeOffset? toStartAtUtc = null,
        DateTimeOffset? since = null)
    {
        return new ExportTaskRequest(
            Code: code,
            DisplayName: $"Task {code}",
            Description: "Nightly bulk import",
            State: state,
            StateReason: null,
            TriggerEvery: TimeSpan.FromHours(24),
            StartAtUtc: toStartAtUtc,
            EndAtUtc: null,
            MaxRunCount: null,
            DataSourceCode: dataSourceCode,
            Parameter: new ExportTaskParameterRequest(
                Type: "Patient",
                Since: since,
                TypeFilterList: ["Patient"]));
    }

    private static ExportTaskUpdateRequest AsUpdateRequest(ExportTaskResponse response) => new(
        DisplayName: response.DisplayName,
        Description: response.Description,
        State: response.State,
        StateReason: response.StateReason,
        DataSourceCode: response.DataSourceCode,
        TriggerEvery: response.TriggerEvery,
        StartAtUtc: response.StartAtUtc,
        EndAtUtc: response.EndAtUtc,
        MaxRunCount: response.MaxRunCount,
        Parameter: response.Parameter);

    [Fact]
    public async Task Create_ValidRequest_Returns201WithCreatedExportTask()
    {
        string dataSourceCode = await CreateDataSourceCodeAsync();
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), dataSourceCode);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/ExportTask", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ExportTaskResponse? created = await response.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(request.Code, created!.Code);
        Assert.Equal(request.DisplayName, created.DisplayName);
        Assert.Equal(TaskTypeId.ExportTask, created.TypeId);
        Assert.Equal(new[] { "Patient" }, created.Parameter.TypeFilterList);
        Assert.Equal(dataSourceCode, created.DataSourceCode);
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
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceCodeAsync());

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/ExportTask", request);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"typeId\":\"ExportTask\"", body);
        Assert.Contains("\"state\":\"Ready\"", body);
        Assert.DoesNotContain("\"typeId\":1", body);
        Assert.DoesNotContain("\"state\":1", body);
    }

    [Fact]
    public async Task Create_WithNonUtcOffsetToStartAtUtc_Succeeds()
    {
        // ExportTaskRequest.StartAtUtc is a DateTimeOffset so it carries its offset
        // explicitly - a plain DateTime? here could otherwise deserialise with Kind=Local for a
        // non-zero offset, which Npgsql rejects for a "timestamp with time zone" column.
        string dataSourceCode = await CreateDataSourceCodeAsync();
        DateTimeOffset toStartAtUtc = new(2026, 9, 15, 8, 0, 0, TimeSpan.FromHours(10));
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), dataSourceCode, toStartAtUtc: toStartAtUtc);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/ExportTask", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ExportTaskResponse? created = await response.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(toStartAtUtc, created!.StartAtUtc);
    }

    [Fact]
    public async Task Create_NonExistentDataSourceCode_Returns400()
    {
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/ExportTask", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateCode_Returns400()
    {
        string code = Guid.NewGuid().ToString();
        string dataSourceCode = await CreateDataSourceCodeAsync();
        HttpResponseMessage firstResponse = await HttpClient.PostAsJsonAsync("/ExportTask", NewRequest(code, dataSourceCode));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/ExportTask", NewRequest(code, dataSourceCode));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ExistingExportTask_ReturnsMatchingExportTask()
    {
        string dataSourceCode = await CreateDataSourceCodeAsync();
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), dataSourceCode);
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportTask", request);
        ExportTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;

        ExportTaskResponse? fetched =
            await HttpClient.GetFromJsonAsync<ExportTaskResponse>($"/ExportTask/{created.Id}", JsonOptions);

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(request.Code, fetched.Code);
    }

    [Fact]
    public async Task GetById_NonExistentExportTask_Returns404()
    {
        HttpResponseMessage response = await HttpClient.GetAsync("/ExportTask/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_AfterCreate_ContainsCreatedExportTask()
    {
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceCodeAsync());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportTask", request);
        ExportTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;

        List<ExportTaskResponse>? all =
            await HttpClient.GetFromJsonAsync<List<ExportTaskResponse>>("/ExportTask", JsonOptions);

        Assert.NotNull(all);
        Assert.Contains(all!, x => x.Id == created.Id);
    }

    [Fact]
    public async Task Search_ByCode_FindsMatchingExportTask()
    {
        string code = Guid.NewGuid().ToString();
        await HttpClient.PostAsJsonAsync("/ExportTask", NewRequest(code, await CreateDataSourceCodeAsync()));

        List<ExportTaskResponse>? results = await HttpClient.GetFromJsonAsync<List<ExportTaskResponse>>(
            $"/ExportTask?code={code}", JsonOptions);

        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal(code, results![0].Code);
    }

    [Fact]
    public async Task Search_ByState_FindsOnlyMatchingState()
    {
        string dataSourceCode = await CreateDataSourceCodeAsync();
        await HttpClient.PostAsJsonAsync("/ExportTask", NewRequest(Guid.NewGuid().ToString(), dataSourceCode, TaskStateId.Ready));
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync(
            "/ExportTask", NewRequest(Guid.NewGuid().ToString(), dataSourceCode, TaskStateId.InProgress));
        ExportTaskResponse inProgress = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;

        List<ExportTaskResponse>? results = await HttpClient.GetFromJsonAsync<List<ExportTaskResponse>>(
            "/ExportTask?state=InProgress", JsonOptions);

        Assert.NotNull(results);
        Assert.Contains(results!, x => x.Id == inProgress.Id);
        Assert.All(results!, x => Assert.Equal(TaskStateId.InProgress, x.State));
    }

    [Fact]
    public async Task Update_ExistingExportTask_PersistsChangesAndPreservesLastStart()
    {
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceCodeAsync());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportTask", request);
        ExportTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;
        // Postgres timestamptz truncates to microsecond precision, so the in-memory CreatedUtc from
        // the POST response has finer resolution than what a DB round-trip will return - fetch the
        // persisted baseline rather than comparing against it directly.
        ExportTaskResponse persistedBaseline =
            (await HttpClient.GetFromJsonAsync<ExportTaskResponse>($"/ExportTask/{created.Id}", JsonOptions))!;

        ExportTaskUpdateRequest updateRequest = AsUpdateRequest(created) with
        {
            DisplayName = "Updated Display Name",
            State = TaskStateId.InProgress,
            StateReason = "Running now",
            Parameter = new ExportTaskParameterRequest(
                Type: "Patient,Organization",
                Since: null,
                TypeFilterList: ["Patient", "Organization"]),
        };
        HttpResponseMessage updateResponse = await HttpClient.PutAsJsonAsync($"/ExportTask/{created.Id}", updateRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        ExportTaskResponse? updated = await updateResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Updated Display Name", updated!.DisplayName);
        Assert.Equal(TaskStateId.InProgress, updated.State);
        Assert.Equal(new[] { "Patient", "Organization" }, updated.Parameter.TypeFilterList);
        Assert.Equal(persistedBaseline.CreatedUtc, updated.CreatedUtc);
        Assert.Null(updated.LastStartUtc);
        Assert.True(updated.UpdatedUtc >= persistedBaseline.UpdatedUtc);
        // Code is not part of the update payload, and DataSourceCode is immutable once set - both must survive unchanged.
        Assert.Equal(created.Code, updated.Code);
        Assert.Equal(created.DataSourceCode, updated.DataSourceCode);

        // The PUT response reflects the tracked in-memory entity - fetch it back to prove the
        // change actually persisted to Postgres.
        ExportTaskResponse? fetched =
            await HttpClient.GetFromJsonAsync<ExportTaskResponse>($"/ExportTask/{created.Id}", JsonOptions);
        Assert.NotNull(fetched);
        Assert.Equal("Updated Display Name", fetched!.DisplayName);
        Assert.Equal(new[] { "Patient", "Organization" }, fetched.Parameter.TypeFilterList);
    }

    [Fact]
    public async Task GetById_ResponseBody_CanBePutStraightBackWithoutModification()
    {
        // The whole point of ExportTaskUpdateRequest excluding the server-controlled fields
        // (Id, TypeId, Code, CreatedUtc, UpdatedUtc, LastStartUtc, LastEndUtc, FailureCount, RunCount,
        // LastCorrelationId) is that a client can round-trip a GET response straight back through PUT
        // without stripping anything out first - the extra JSON properties are just ignored. A
        // non-null Since is set here because the GET response converts it (and every other time
        // value) to ServiceDefaultTimeZone's offset (e.g. +10:00) - Npgsql rejects a non-UTC
        // DateTimeOffset for a "timestamp with time zone" column, so the PUT handler must normalise
        // it back to UTC before persisting rather than writing the round-tripped offset straight through.
        ExportTaskRequest request = NewRequest(
            Guid.NewGuid().ToString(),
            await CreateDataSourceCodeAsync(),
            since: DateTimeOffset.UtcNow.AddDays(-1));
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportTask", request);
        ExportTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"/ExportTask/{created.Id}");
        string unmodifiedGetBody = await getResponse.Content.ReadAsStringAsync();
        // The service default time zone (+10:00 in test config) must actually be present on the
        // wire here - otherwise this test would not be exercising the offset-normalisation bug.
        Assert.Contains("+10:00", unmodifiedGetBody);

        HttpResponseMessage putResponse = await HttpClient.PutAsync(
            $"/ExportTask/{created.Id}",
            new StringContent(unmodifiedGetBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        ExportTaskResponse? updated = await putResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal(created.DisplayName, updated!.DisplayName);
        Assert.Equal(created.Code, updated.Code);
        Assert.Equal(created.DataSourceCode, updated.DataSourceCode);
        // Postgres timestamptz truncates to microsecond precision, so the in-memory Since carried on
        // created (never round-tripped through the DB) can be a fraction of a microsecond ahead of
        // updated's (read back after the PUT's SaveChanges) - assert the round-trip preserved the
        // same instant rather than bit-for-bit equality.
        Assert.NotNull(updated.Parameter.Since);
        Assert.True((created.Parameter.Since!.Value - updated.Parameter.Since!.Value).Duration() < TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Update_NonExistentExportTask_Returns404()
    {
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceCodeAsync());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportTask", request);
        ExportTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;
        ExportTaskUpdateRequest updateRequest = AsUpdateRequest(created);

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync("/ExportTask/999999", updateRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingExportTask_Returns204ThenGetByIdReturns404()
    {
        ExportTaskRequest request = NewRequest(Guid.NewGuid().ToString(), await CreateDataSourceCodeAsync());
        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync("/ExportTask", request);
        ExportTaskResponse created = (await createResponse.Content.ReadFromJsonAsync<ExportTaskResponse>(JsonOptions))!;

        HttpResponseMessage deleteResponse = await HttpClient.DeleteAsync($"/ExportTask/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await HttpClient.GetAsync($"/ExportTask/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_NonExistentExportTask_Returns404()
    {
        HttpResponseMessage response = await HttpClient.DeleteAsync("/ExportTask/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
