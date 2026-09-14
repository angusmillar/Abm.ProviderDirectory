using Abm.Core.Time;
using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Abm.PD.Core.Api.Endpoints;

public static class ExportLoaderTaskEndpoints
{
    public static IEndpointRouteBuilder MapExportLoaderTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ExportLoaderTask", GetAllOrSearch);
        endpoints.MapGet("/ExportLoaderTask/{id:int}", GetById);
        endpoints.MapPost("/ExportLoaderTask", Create);
        endpoints.MapPut("/ExportLoaderTask/{id:int}", Update);
        endpoints.MapDelete("/ExportLoaderTask/{id:int}", Delete);

        return endpoints;
    }

    private static async Task<IResult> GetAllOrSearch(
        string? code,
        TaskStateId? state,
        [FromQuery(Name = "last-start-from")] DateTime? lastStartFrom,
        [FromQuery(Name = "last-start-to")] DateTime? lastStartTo,
        IExportLoaderTaskRepository exportLoaderTaskRepository,
        IOptions<TimeSettings> timeSettings,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExportLoaderTask> exportLoaderTaskList = code is null && state is null && lastStartFrom is null && lastStartTo is null
            ? await exportLoaderTaskRepository.GetAllAsync(cancellationToken)
            : await exportLoaderTaskRepository.SearchAsync(
                code: code,
                state: state,
                lastStartFrom: lastStartFrom,
                lastStartTo: lastStartTo,
                cancellationToken: cancellationToken);

        return Results.Ok(exportLoaderTaskList.Select(
            x => ExportLoaderTaskResponse.FromEntity(x, timeSettings.Value.ServiceDefaultTimeZone)));
    }

    private static async Task<IResult> GetById(
        int id,
        IExportLoaderTaskRepository exportLoaderTaskRepository,
        IOptions<TimeSettings> timeSettings,
        CancellationToken cancellationToken)
    {
        ExportLoaderTask? exportLoaderTask = await exportLoaderTaskRepository.GetByIdAsync(id, cancellationToken);
        return exportLoaderTask is null
            ? Results.NotFound()
            : Results.Ok(ExportLoaderTaskResponse.FromEntity(exportLoaderTask, timeSettings.Value.ServiceDefaultTimeZone));
    }

    private static async Task<IResult> Create(
        ExportLoaderTaskRequest request,
        IExportLoaderTaskRepository exportLoaderTaskRepository,
        IDataSourceRepository dataSourceRepository,
        IOptions<TimeSettings> timeSettings,
        CancellationToken cancellationToken)
    {
        var dataSourceList = await dataSourceRepository.SearchAsync(code: request.DataSourceCode.Trim(), displayName: null, cancellationToken);
        if (dataSourceList.Count == 0)
        {
            return Results.BadRequest($"DataSourceCode {request.DataSourceCode} does not exist");
        }

        // Code carries a unique index at the database level - checking first turns what would
        // otherwise surface as an unhandled DbUpdateException on SaveChangesAsync into a clear 400.
        IReadOnlyList<ExportLoaderTask> existingWithCode = await exportLoaderTaskRepository.SearchAsync(
            code: request.Code,
            state: null,
            lastStartFrom: null,
            lastStartTo: null,
            cancellationToken: cancellationToken);
        if (existingWithCode.Count > 0)
        {
            return Results.BadRequest($"ExportLoaderTask with Code '{request.Code}' already exists");
        }

        DateTime nowUtc = DateTime.UtcNow;
        ExportLoaderTask exportLoaderTask = new()
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
            ToStartAtUtc = request.ToStartAtUtc?.UtcDateTime,
            ToEndAtUtc = request.ToEndAtUtc?.UtcDateTime,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStart = null,
            LastEnd = null,
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
        exportLoaderTask = await exportLoaderTaskRepository.AddAsync(exportLoaderTask, cancellationToken);
        return Results.Created(
            $"/ExportLoaderTask/{exportLoaderTask.Id}",
            ExportLoaderTaskResponse.FromEntity(exportLoaderTask, timeSettings.Value.ServiceDefaultTimeZone));
    }

    private static async Task<IResult> Update(
        int id,
        ExportLoaderTaskUpdateRequest request,
        IExportLoaderTaskRepository exportLoaderTaskRepository,
        IOptions<TimeSettings> timeSettings,
        CancellationToken cancellationToken)
    {
        // Code is deliberately absent from ExportLoaderTaskUpdateRequest - it is immutable after
        // creation. DataSourceCode is present but is also immutable after creation, so it is
        // validated below against the existing row rather than copied across.
        ExportLoaderTask? existing = await exportLoaderTaskRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }
        
        if (existing.State == TaskStateId.InProgress)
        {
            return Results.BadRequest($"The ExportLoaderTask State={existing.State}', " +
                                      $"can not modify a task while {nameof(TaskStateId.InProgress)} ");
        }
        
        if (!existing.DataSource.Code.Equals(request.DataSourceCode.Trim()))
        {
            return Results.BadRequest($"The ExportLoaderTask's DataSourceCode: {request.DataSourceCode.Trim()}', " +
                                      $"can not be updated. Current DataSourceCode is {existing.DataSource.Code} ");
        }

        existing.DisplayName = request.DisplayName;
        existing.Description = request.Description;
        existing.State = request.State;
        existing.StateReason = request.StateReason;
        existing.TriggerEvery = request.TriggerEvery;
        existing.ToStartAtUtc = request.ToStartAtUtc?.UtcDateTime;
        existing.ToEndAtUtc = request.ToEndAtUtc?.UtcDateTime;
        existing.UpdatedUtc = DateTime.UtcNow;
        existing.Parameter.Type = request.Parameter.Type;
        // Npgsql only accepts DateTimeOffset.Offset == 0 for a "timestamp with time zone" column -
        // a round-tripped GET response carries ServiceDefaultTimeZone's offset (e.g. +10:00), so
        // normalise back to UTC before storing.
        existing.Parameter.Since = request.Parameter.Since?.ToUniversalTime();
        existing.Parameter.TypeFilterList = request.Parameter.TypeFilterList;

        ExportLoaderTask? updated = await exportLoaderTaskRepository.UpdateAsync(id, existing, cancellationToken);
        if (updated is null)
        {
            return Results.NotFound();
        }

        // UpdateAsync's internal re-fetch doesn't Include the DataSource navigation, and DataSourceId
        // never changes via update, so carry it over from the already-loaded `existing` rather than
        // returning a response with a null DataSource.
        updated.DataSource = existing.DataSource;
        return Results.Ok(ExportLoaderTaskResponse.FromEntity(updated, timeSettings.Value.ServiceDefaultTimeZone));
    }

    private static async Task<IResult> Delete(
        int id,
        IExportLoaderTaskRepository exportLoaderTaskRepository,
        CancellationToken cancellationToken)
    {
        bool deleted = await exportLoaderTaskRepository.DeleteAsync(id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
