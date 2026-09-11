using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Api.Endpoints;

public static class ResourceEndpoints
{
    public static IEndpointRouteBuilder MapResourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/Resource", GetAllOrSearch);
        endpoints.MapGet("/Resource/{resourceId}", GetById);
        endpoints.MapPost("/Resource", Create);
        endpoints.MapPut("/Resource/{resourceId}", Update);
        endpoints.MapDelete("/Resource/{resourceId}", Delete);

        return endpoints;
    }

    private static async Task<IResult> GetAllOrSearch(
        string? type,
        string? id,
        IResourceRepository resourceRepository,
        CancellationToken cancellationToken)
    {
        if (type is null && id is null)
        {
            return Results.Ok(await resourceRepository.GetAllAsync(cancellationToken));
        }

        return Results.Ok(await resourceRepository.SearchAsync(type, id, cancellationToken));
    }

    private static async Task<IResult> GetById(
        string resourceId,
        IResourceRepository resourceRepository,
        CancellationToken cancellationToken)
    {
        Resource? resource = await resourceRepository.GetByIdAsync(resourceId, cancellationToken);
        return resource is null
            ? Results.NotFound()
            : Results.Ok(resource);
    }

    private static async Task<IResult> Create(
        ResourceRequest request,
        IResourceRepository resourceRepository,
        CancellationToken cancellationToken)
    {
        Resource resource = new()
        {
            ResourceType = request.ResourceType,
            ResourceId = request.ResourceId,
        };
        resource = await resourceRepository.AddAsync(resource, cancellationToken);
        return Results.Created($"/Resource/{resource.ResourceId}", resource);
    }

    private static async Task<IResult> Update(
        string resourceId,
        ResourceRequest request,
        IResourceRepository resourceRepository,
        CancellationToken cancellationToken)
    {
        Resource? resource = await resourceRepository.UpdateAsync(
            resourceId: resourceId,
            resourceType: request.ResourceType,
            newResourceId: request.ResourceId,
            cancellationToken: cancellationToken);
        return resource is null
            ? Results.NotFound()
            : Results.Ok(resource);
    }

    private static async Task<IResult> Delete(
        string resourceId,
        IResourceRepository resourceRepository,
        CancellationToken cancellationToken)
    {
        bool deleted = await resourceRepository.DeleteAsync(resourceId, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
