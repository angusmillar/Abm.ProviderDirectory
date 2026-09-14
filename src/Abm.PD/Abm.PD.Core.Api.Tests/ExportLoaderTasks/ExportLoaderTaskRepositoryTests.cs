using Abm.PD.Core.Api.Tests.Fixtures;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Abm.PD.Core.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abm.PD.Core.Api.Tests.ExportLoaderTasks;

public class ExportLoaderTaskRepositoryTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly IntegrationTestFixture Fixture = fixture;

    private static ExportLoaderTask NewTask(
        string code,
        TaskStateId state = TaskStateId.Ready,
        DateTime? lastStart = null,
        TimeSpan? triggerEvery = null,
        DateTime? toStartAtUtc = null,
        DateTime? toEndAtUtc = null,
        int? dataSourceId = null,
        int failureCount = 0)
    {
        DateTime nowUtc = DateTime.UtcNow;
        return new ExportLoaderTask
        {
            Code = code,
            DisplayName = $"Task {code}",
            Description = null,
            State = state,
            StateReason = null,
            TriggerEvery = triggerEvery ?? TimeSpan.FromHours(24),
            ToStartAtUtc = toStartAtUtc,
            ToEndAtUtc = toEndAtUtc,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStart = lastStart,
            LastEnd = null,
            FailureCount = failureCount,
            // When no existing DataSourceId is supplied, a fresh, unsaved DataSource is attached via
            // the navigation property - EF's graph tracking inserts it in the same SaveChanges call
            // that adds the task. Passing an existing id (the update-payload case) skips that: the
            // navigation is left null since only DataSourceId is read back off this transient object.
            DataSourceId = dataSourceId ?? 0,
            DataSource = dataSourceId is null
                ? new DataSource { Code = Guid.NewGuid().ToString(), DisplayName = "Test Data Source" }
                : null!,
            Parameter = new ExportParameter
            {
                Type = "Patient",
                Since = null,
                TypeFilterList = ["Patient"],
            },
        };
    }

    [Fact]
    public async Task AddAsync_NewTask_PersistsAndReturnsWithId()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();

        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.True(added.Id > 0);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingTask_ReturnsMatchingTaskWithParameter()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(added.Code, fetched!.Code);
        Assert.Equal(new[] { "Patient" }, fetched.Parameter.TypeFilterList);
    }

    [Fact]
    public async Task GetAllAsync_AfterAdd_ContainsAddedTask()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> all = await repository.GetAllAsync(CancellationToken.None);

        Assert.Contains(all, x => x.Id == added.Id);
    }

    [Fact]
    public async Task UpdateAsync_ExistingTask_PersistsChangesIncludingParameter()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        ExportLoaderTask update = NewTask(added.Code, TaskStateId.InProgress, dataSourceId: added.DataSourceId);
        update.Parameter.TypeFilterList = ["Patient", "Organization"];
        // A fixed value clear of DateTime.UtcNow's sub-microsecond precision, so it round-trips
        // through the timestamptz column (microsecond precision) without truncation flakiness.
        DateTime newUpdatedUtc = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        update.UpdatedUtc = newUpdatedUtc;
        ExportLoaderTask? updated = await repository.UpdateAsync(added.Id, update, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(TaskStateId.InProgress, updated!.State);

        ExportLoaderTask? fetched = await repository.GetByIdAsync(added.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(TaskStateId.InProgress, fetched!.State);
        Assert.Equal(new[] { "Patient", "Organization" }, fetched.Parameter.TypeFilterList);
        Assert.Equal(newUpdatedUtc, fetched.UpdatedUtc);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentTask_ReturnsNull()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();

        ExportLoaderTask? updated = await repository.UpdateAsync(999999, NewTask("missing"), CancellationToken.None);

        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteAsync_ExistingTask_RemovesItThenGetByIdReturnsNull()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask added = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        bool deleted = await repository.DeleteAsync(added.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await repository.GetByIdAsync(added.Id, CancellationToken.None));

        // The owned Parameter now lives in its own table rather than table-split into "task" - assert
        // the FK cascade actually removed its row too, not just that the parent is unreachable.
        ProviderDirectoryDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ProviderDirectoryDbContext>();
        long remainingParameterRows = await dbContext.Database
            .SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM export_loader_task_parameter WHERE export_loader_task_id = {added.Id}")
            .SingleAsync();
        Assert.Equal(0, remainingParameterRows);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentTask_ReturnsFalse()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();

        bool deleted = await repository.DeleteAsync(999999, CancellationToken.None);

        Assert.False(deleted);
    }

    [Fact]
    public async Task SearchAsync_WithNoFilters_ReturnsAllTasks()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        ExportLoaderTask task1 = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
        ExportLoaderTask task2 = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);
        ExportLoaderTask task3 = await repository.AddAsync(NewTask(Guid.NewGuid().ToString()), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> results = await repository.SearchAsync(
            code: null,
            state: null,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: CancellationToken.None);

        Assert.Contains(results, x => x.Id == task1.Id);
        Assert.Contains(results, x => x.Id == task2.Id);
        Assert.Contains(results, x => x.Id == task3.Id);
    }

    [Fact]
    public async Task SearchAsync_ByCode_FindsMatchingTask()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        string code = Guid.NewGuid().ToString();
        await repository.AddAsync(NewTask(code), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> results = await repository.SearchAsync(
            code: code,
            state: null,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(code, results[0].Code);
    }

    [Fact]
    public async Task SearchAsync_ByState_FindsOnlyMatchingState()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        await repository.AddAsync(NewTask(Guid.NewGuid().ToString(), TaskStateId.Ready), CancellationToken.None);
        ExportLoaderTask inProgress = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), TaskStateId.InProgress), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> results = await repository.SearchAsync(
            code: null,
            state: TaskStateId.InProgress,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: CancellationToken.None);

        Assert.Contains(results, x => x.Id == inProgress.Id);
        Assert.All(results, x => Assert.Equal(TaskStateId.InProgress, x.State));
    }

    [Fact]
    public async Task SearchAsync_ByLastStartRange_FindsOnlyTasksWithinRange()
    {
        using IServiceScope scope = Fixture.Services.CreateScope();
        IExportLoaderTaskRepository repository =
            scope.ServiceProvider.GetRequiredService<IExportLoaderTaskRepository>();
        DateTime inRange = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        DateTime outOfRange = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime rangeFrom = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime rangeTo = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        ExportLoaderTask matching = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), lastStart: inRange), CancellationToken.None);
        ExportLoaderTask nonMatching = await repository.AddAsync(
            NewTask(Guid.NewGuid().ToString(), lastStart: outOfRange), CancellationToken.None);

        IReadOnlyList<ExportLoaderTask> results = await repository.SearchAsync(
            code: null,
            state: null,
            lastStartFrom: rangeFrom,
            lastStartTo: rangeTo,
            cancellationToken: CancellationToken.None);

        Assert.Contains(results, x => x.Id == matching.Id);
        Assert.DoesNotContain(results, x => x.Id == nonMatching.Id);
        Assert.All(results, x => Assert.InRange(x.LastStart!.Value, rangeFrom, rangeTo));
    }
}
