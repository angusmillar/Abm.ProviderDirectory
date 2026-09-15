using Abm.Core.Time;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Abm.PD.Core.Api.Endpoints;

public static class ExportTaskEndpoints
{
    public static IEndpointRouteBuilder MapExportTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ExportTask", GetAllOrSearch);
        endpoints.MapGet("/ExportTask/{id:int}", GetById);
        endpoints.MapPost("/ExportTask", Create);
        endpoints.MapPut("/ExportTask/{id:int}", Update);
        endpoints.MapDelete("/ExportTask/{id:int}", Delete);

        return endpoints;
    }

    private static async Task<IResult> GetAllOrSearch(
        string? code,
        TaskStateId? state,
        [FromQuery(Name = "last-start-from")] DateTime? lastStartFrom,
        [FromQuery(Name = "last-start-to")] DateTime? lastStartTo,
        IExportTaskRepository exportTaskRepository,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExportTask> exportTaskList = code is null && state is null && lastStartFrom is null && lastStartTo is null
            ? await exportTaskRepository.GetAllAsync(cancellationToken)
            : await exportTaskRepository.SearchAsync(
                code: code,
                state: state,
                lastStartFrom: lastStartFrom,
                lastStartTo: lastStartTo,
                cancellationToken: cancellationToken);

        return Results.Ok(exportTaskList.Select(
            x => ExportTaskResponse.FromEntity(x, dateTimeProvider)));
    }

    private static async Task<IResult> GetById(
        int id,
        IExportTaskRepository exportTaskRepository,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        ExportTask? exportTask = await exportTaskRepository.GetByIdAsync(id, cancellationToken);
        return exportTask is null
            ? Results.NotFound()
            : Results.Ok(ExportTaskResponse.FromEntity(exportTask, dateTimeProvider));
    }

    private static async Task<IResult> Create(
        ExportTaskRequest request,
        IExportTaskRepository exportTaskRepository,
        IDataSourceRepository dataSourceRepository,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        var dataSourceList = await dataSourceRepository.SearchAsync(code: request.DataSourceCode.Trim(), displayName: null, cancellationToken);
        if (dataSourceList.Count == 0)
        {
            return Results.BadRequest($"DataSourceCode {request.DataSourceCode} does not exist");
        }

        // Code carries a unique index at the database level - checking first turns what would
        // otherwise surface as an unhandled DbUpdateException on SaveChangesAsync into a clear 400.
        IReadOnlyList<ExportTask> existingWithCode = await exportTaskRepository.SearchAsync(
            code: request.Code,
            state: null,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: cancellationToken);
        if (existingWithCode.Count > 0)
        {
            return Results.BadRequest($"ExportTask with Code '{request.Code}' already exists");
        }

        DateTime nowUtc = DateTime.UtcNow;
        ExportTask exportTask = new()
        {
            Code = request.Code,
            DisplayName = request.DisplayName,
            Description = request.Description,
            State = request.State,
            StateReason = request.StateReason,
            TriggerEvery = request.TriggerEvery,
            // DateTimeOffset.UtcDateTime always yields Kind=Utc regardless of the offset the caller
            // sent - Npgsql rejects a DateTime with Kind=Local or Unspecified for a "timestamp with
            // time zone" column, which a plain DateTime? here could otherwise carry depending on how
            // the incoming JSON's offset was parsed.
            StartAtUtc = request.StartAtUtc?.UtcDateTime,
            EndAtUtc = request.EndAtUtc?.UtcDateTime,
            MaxRunCount = request.MaxRunCount,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStartUtc = null,
            LastEndUtc = null,
            DataSourceId = dataSourceList.First().Id,
            DataSource = dataSourceList.First(),
            Parameter = new ExportParameter
            {
                Type = request.Parameter.Type,
                // Npgsql only accepts DateTimeOffset.Offset == 0 for a "timestamp with time zone"
                // column - the caller may have sent any offset, so normalise to UTC before storing.
                Since = request.Parameter.Since?.ToUniversalTime(),
                TypeFilterList = request.Parameter.TypeFilterList,
            },
        };
        exportTask = await exportTaskRepository.AddAsync(exportTask, cancellationToken);
        return Results.Created(
            $"/ExportTask/{exportTask.Id}",
            ExportTaskResponse.FromEntity(exportTask, dateTimeProvider));
    }

    private static async Task<IResult> Update(
        int id,
        ExportTaskUpdateRequest request,
        IExportTaskRepository exportTaskRepository,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        // Code is deliberately absent from ExportTaskUpdateRequest - it is immutable after
        // creation. DataSourceCode is present but is also immutable after creation, so it is
        // validated below against the existing row rather than copied across.
        ExportTask? existing = await exportTaskRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (existing.State == TaskStateId.InProgress)
        {
            return Results.BadRequest($"The ExportTask State={existing.State}', " +
                                      $"can not modify a task while {nameof(TaskStateId.InProgress)} ");
        }

        if (!existing.DataSource.Code.Equals(request.DataSourceCode.Trim()))
        {
            return Results.BadRequest($"The ExportTask's DataSourceCode: {request.DataSourceCode.Trim()}', " +
                                      $"can not be updated. Current DataSourceCode is {existing.DataSource.Code} ");
        }

        existing.DisplayName = request.DisplayName;
        existing.Description = request.Description;
        existing.State = request.State;
        existing.StateReason = request.StateReason;
        existing.TriggerEvery = request.TriggerEvery;
        existing.StartAtUtc = request.StartAtUtc?.UtcDateTime;
        existing.EndAtUtc = request.EndAtUtc?.UtcDateTime;
        existing.MaxRunCount = request.MaxRunCount;
        existing.UpdatedUtc = DateTime.UtcNow;
        existing.Parameter.Type = request.Parameter.Type;
        // Npgsql only accepts DateTimeOffset.Offset == 0 for a "timestamp with time zone" column -
        // a round-tripped GET response carries ServiceDefaultTimeZone's offset (e.g. +10:00), so
        // normalise back to UTC before storing.
        existing.Parameter.Since = request.Parameter.Since?.ToUniversalTime();
        existing.Parameter.TypeFilterList = request.Parameter.TypeFilterList;

        ExportTask? updated = await exportTaskRepository.UpdateAsync(id, existing, cancellationToken);
        if (updated is null)
        {
            return Results.NotFound();
        }

        // UpdateAsync's internal re-fetch doesn't Include the DataSource navigation, and DataSourceId
        // never changes via update, so carry it over from the already-loaded `existing` rather than
        // returning a response with a null DataSource.
        updated.DataSource = existing.DataSource;
        return Results.Ok(ExportTaskResponse.FromEntity(updated, dateTimeProvider));
    }

    private static async Task<IResult> Delete(
        int id,
        IExportTaskRepository exportTaskRepository,
        CancellationToken cancellationToken)
    {
        bool deleted = await exportTaskRepository.DeleteAsync(id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
