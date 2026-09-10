using Abm.PD.Core.Api.Contracts;
using Abm.PD.Core.Domain.Entities;
using Abm.PD.Core.Domain.Repositories;

namespace Abm.PD.Core.Api.Endpoints;

public static class ResourceEndpoints
{
    public static IEndpointRouteBuilder MapResourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/resources", GetAll);
        endpoints.MapGet("/resources/search", Search);
        endpoints.MapGet("/resources/{id:int}", GetById);
        endpoints.MapPost("/resources", Create);
        endpoints.MapPut("/resources/{id:int}", Update);
        endpoints.MapDelete("/resources/{id:int}", Delete);

        return endpoints;
    }

    private static async Task<IResult> GetAll(
        IResourceRepository resourceRepository,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await resourceRepository.GetAllAsync(cancellationToken));
    }

    private static async Task<IResult> Search(
        string? resourceType,
        string? resourceId,
        IResourceRepository resourceRepository,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await resourceRepository.SearchAsync(resourceType, resourceId, cancellationToken));
    }

    private static async Task<IResult> GetById(
        int id,
        IResourceRepository resourceRepository,
        CancellationToken cancellationToken)
    {
        Resource? resource = await resourceRepository.GetByIdAsync(id, cancellationToken);
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
        return Results.Created($"/resources/{resource.Id}", resource);
    }

    private static async Task<IResult> Update(
        int id,
        ResourceRequest request,
        IResourceRepository resourceRepository,
        CancellationToken cancellationToken)
    {
        Resource? resource = await resourceRepository.UpdateAsync(
            id: id,
            resourceType: request.ResourceType,
            resourceId: request.ResourceId,
            cancellationToken: cancellationToken);
        return resource is null
            ? Results.NotFound()
            : Results.Ok(resource);
    }

    private static async Task<IResult> Delete(
        int id,
        IResourceRepository resourceRepository,
        CancellationToken cancellationToken)
    {
        bool deleted = await resourceRepository.DeleteAsync(id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
