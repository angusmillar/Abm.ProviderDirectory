using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Enums;
using Abm.PD.Core.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

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
        CancellationToken cancellationToken)
    {
        if (code is null && state is null && lastStartFrom is null && lastStartTo is null)
        {
            return Results.Ok(await exportLoaderTaskRepository.GetAllAsync(cancellationToken));
        }

        return Results.Ok(await exportLoaderTaskRepository.SearchAsync(
            code: code,
            state: state,
            lastStartFrom: lastStartFrom,
            lastStartTo: lastStartTo,
            cancellationToken: cancellationToken));
    }

    private static async Task<IResult> GetById(
        int id,
        IExportLoaderTaskRepository exportLoaderTaskRepository,
        CancellationToken cancellationToken)
    {
        ExportLoaderTask? exportLoaderTask = await exportLoaderTaskRepository.GetByIdAsync(id, cancellationToken);
        return exportLoaderTask is null
            ? Results.NotFound()
            : Results.Ok(exportLoaderTask);
    }

    private static async Task<IResult> Create(
        ExportLoaderTaskRequest request,
        IExportLoaderTaskRepository exportLoaderTaskRepository,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;
        ExportLoaderTask exportLoaderTask = new()
        {
            Code = request.Code,
            DisplayName = request.DisplayName,
            Description = request.Description,
            State = request.State,
            StateReason = request.StateReason,
            TriggerEvery = request.TriggerEvery,
            ToStartAtUtc = request.ToStartAtUtc,
            ToEndAtUtc = request.ToEndAtUtc,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            LastStart = null,
            LastEnd = null,
            Parameter = new ExportParameter
            {
                Type = request.Parameter.Type,
                Since = request.Parameter.Since,
                TypeFilterList = request.Parameter.TypeFilterList,
            },
        };
        exportLoaderTask = await exportLoaderTaskRepository.AddAsync(exportLoaderTask, cancellationToken);
        return Results.Created($"/ExportLoaderTask/{exportLoaderTask.Id}", exportLoaderTask);
    }

    private static async Task<IResult> Update(
        int id,
        ExportLoaderTaskRequest request,
        IExportLoaderTaskRepository exportLoaderTaskRepository,
        CancellationToken cancellationToken)
    {
        // The repository's UpdateAsync copies LastStart/LastEnd/CreatedUtc straight from whatever
        // entity it is given, so the current row is fetched first to carry those loader-owned fields
        // through unchanged rather than the request DTO (which deliberately has no place for them)
        // wiping them out.
        ExportLoaderTask? existing = await exportLoaderTaskRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        existing.Code = request.Code;
        existing.DisplayName = request.DisplayName;
        existing.Description = request.Description;
        existing.State = request.State;
        existing.StateReason = request.StateReason;
        existing.TriggerEvery = request.TriggerEvery;
        existing.ToStartAtUtc = request.ToStartAtUtc;
        existing.ToEndAtUtc = request.ToEndAtUtc;
        existing.UpdatedUtc = DateTime.UtcNow;
        existing.Parameter.Type = request.Parameter.Type;
        existing.Parameter.Since = request.Parameter.Since;
        existing.Parameter.TypeFilterList = request.Parameter.TypeFilterList;

        ExportLoaderTask? updated = await exportLoaderTaskRepository.UpdateAsync(id, existing, cancellationToken);
        return updated is null
            ? Results.NotFound()
            : Results.Ok(updated);
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
