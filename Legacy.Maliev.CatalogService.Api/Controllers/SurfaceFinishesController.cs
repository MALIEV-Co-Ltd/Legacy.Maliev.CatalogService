using Legacy.Maliev.CatalogService.Api.Authorization;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CatalogService.Api.Controllers;

/// <summary>Preserves the legacy material surface finish contracts.</summary>
[ApiController]
[Route("materials/[controller]")]
[Authorize]
public sealed class SurfaceFinishesController(ICatalogService service) : ControllerBase
{
    /// <summary>Creates a surface finish.</summary>
    [HttpPost]
    [RequirePermission(CatalogPermissions.SurfaceFinishesCreate)]
    public async Task<ActionResult> CreateSurfaceFinishAsync(UpsertSurfaceFinishRequest request, CancellationToken cancellationToken)
    {
        var created = await service.CreateSurfaceFinishAsync(request, cancellationToken);
        return CreatedAtRoute("GetSurfaceFinish", new { id = created.Id }, created);
    }

    /// <summary>Links a surface finish to a material.</summary>
    [HttpPost("/materials/{materialId:int}/surfacefinishes/{surfaceFinishId:int}")]
    [RequirePermission(CatalogPermissions.MaterialsUpdate)]
    public async Task<ActionResult> CreateSurfaceFinishForMaterialAsync(int materialId, int surfaceFinishId, CancellationToken cancellationToken)
    {
        if (await service.GetMaterialAsync(materialId, cancellationToken) is null || await service.GetSurfaceFinishAsync(surfaceFinishId, cancellationToken) is null)
        {
            return NotFound();
        }

        var created = await service.CreateMaterialSurfaceFinishAsync(materialId, surfaceFinishId, cancellationToken);
        return created is null
            ? NoContent()
            : CreatedAtRoute("GetMaterialHasSurfaceFinish", new { materialId, surfaceFinishId }, created);
    }

    /// <summary>Deletes a surface finish.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(CatalogPermissions.SurfaceFinishesDelete)]
    public async Task<ActionResult> DeleteSurfaceFinishAsync(int id, CancellationToken cancellationToken) =>
        await service.DeleteSurfaceFinishAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Deletes a material-surface-finish link.</summary>
    [HttpDelete("/materials/{materialId:int}/surfacefinishes/{surfaceFinishId:int}")]
    [RequirePermission(CatalogPermissions.MaterialsUpdate)]
    public async Task<ActionResult> DeleteSurfaceFinishForMaterialAsync(int materialId, int surfaceFinishId, CancellationToken cancellationToken) =>
        await service.DeleteMaterialSurfaceFinishAsync(materialId, surfaceFinishId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Returns all surface finishes.</summary>
    [HttpGet]
    [RequirePermission(CatalogPermissions.SurfaceFinishesRead)]
    public async Task<ActionResult<IReadOnlyList<SurfaceFinishResponse>>> GetSurfaceFinishesAsync(CancellationToken cancellationToken)
    {
        var finishes = await service.GetSurfaceFinishesAsync(cancellationToken);
        return finishes.Count == 0 ? NotFound() : Ok(finishes);
    }

    /// <summary>Returns finishes linked to a material.</summary>
    [HttpGet("/materials/{id:int}/surfacefinishes")]
    [RequirePermission(CatalogPermissions.MaterialsRead)]
    public async Task<ActionResult<IReadOnlyList<SurfaceFinishResponse>>> GetMaterialSurfaceFinishesAsync(int id, CancellationToken cancellationToken)
    {
        var finishes = await service.GetMaterialSurfaceFinishesAsync(id, cancellationToken);
        return finishes is null || finishes.Count == 0 ? NotFound() : Ok(finishes);
    }

    /// <summary>Returns a surface finish.</summary>
    [HttpGet("{id:int}", Name = "GetSurfaceFinish")]
    [RequirePermission(CatalogPermissions.SurfaceFinishesRead)]
    public async Task<ActionResult<SurfaceFinishResponse>> GetSurfaceFinishAsync(int id, CancellationToken cancellationToken)
    {
        var finish = await service.GetSurfaceFinishAsync(id, cancellationToken);
        return finish is null ? NotFound() : finish;
    }

    /// <summary>Returns a material-surface-finish link.</summary>
    [HttpGet("/materials/{materialId:int}/surfacefinishes/{surfaceFinishId:int}", Name = "GetMaterialHasSurfaceFinish")]
    [RequirePermission(CatalogPermissions.MaterialsRead)]
    public async Task<ActionResult<MaterialSurfaceFinishResponse>> GetSurfaceFinishesForMaterialAsync(int materialId, int surfaceFinishId, CancellationToken cancellationToken)
    {
        var link = await service.GetMaterialSurfaceFinishAsync(materialId, surfaceFinishId, cancellationToken);
        return link is null ? NotFound() : link;
    }

    /// <summary>Updates a surface finish.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(CatalogPermissions.SurfaceFinishesUpdate)]
    public async Task<ActionResult> UpdateSurfaceFinishAsync(int id, UpsertSurfaceFinishRequest request, CancellationToken cancellationToken) =>
        await service.UpdateSurfaceFinishAsync(id, request, cancellationToken) ? NoContent() : NotFound();
}
