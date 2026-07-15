using Legacy.Maliev.CatalogService.Api.Authorization;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CatalogService.Api.Controllers;

/// <summary>Preserves the legacy material color contracts.</summary>
[ApiController]
[Route("materials/[controller]")]
[Authorize]
public sealed class ColorsController(ICatalogService service) : ControllerBase
{
    /// <summary>Creates a color.</summary>
    [HttpPost]
    [RequirePermission(CatalogPermissions.ColorsCreate)]
    public async Task<ActionResult> CreateColorAsync(UpsertColorRequest request, CancellationToken cancellationToken)
    {
        var created = await service.CreateColorAsync(request, cancellationToken);
        return CreatedAtRoute("GetColor", new { id = created.Id }, created);
    }

    /// <summary>Links a color to a material.</summary>
    [HttpPost("/materials/{materialId:int}/colors/{colorId:int}")]
    [RequirePermission(CatalogPermissions.MaterialsUpdate)]
    public async Task<ActionResult> CreateColorsForMaterialAsync(int materialId, int colorId, CancellationToken cancellationToken)
    {
        if (await service.GetMaterialAsync(materialId, cancellationToken) is null || await service.GetColorAsync(colorId, cancellationToken) is null)
        {
            return NotFound();
        }

        var created = await service.CreateMaterialColorAsync(materialId, colorId, cancellationToken);
        return created is null
            ? NoContent()
            : CreatedAtRoute("GetMaterialHasColor", new { materialId, colorId }, created);
    }

    /// <summary>Deletes a color.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(CatalogPermissions.ColorsDelete)]
    public async Task<ActionResult> DeleteColorAsync(int id, CancellationToken cancellationToken) =>
        await service.DeleteColorAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Deletes a material-color link.</summary>
    [HttpDelete("/materials/{materialId:int}/colors/{colorId:int}")]
    [RequirePermission(CatalogPermissions.MaterialsUpdate)]
    public async Task<ActionResult> DeleteColorsForMaterialAsync(int materialId, int colorId, CancellationToken cancellationToken) =>
        await service.DeleteMaterialColorAsync(materialId, colorId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Returns all colors.</summary>
    [HttpGet]
    [RequirePermission(CatalogPermissions.ColorsRead)]
    public async Task<ActionResult<IReadOnlyList<ColorResponse>>> GetColorsAsync(CancellationToken cancellationToken)
    {
        var colors = await service.GetColorsAsync(cancellationToken);
        return colors.Count == 0 ? NotFound() : Ok(colors);
    }

    /// <summary>Returns a color.</summary>
    [HttpGet("{id:int}", Name = "GetColor")]
    [RequirePermission(CatalogPermissions.ColorsRead)]
    public async Task<ActionResult<ColorResponse>> GetColorAsync(int id, CancellationToken cancellationToken)
    {
        var color = await service.GetColorAsync(id, cancellationToken);
        return color is null ? NotFound() : color;
    }

    /// <summary>Returns a material-color link.</summary>
    [HttpGet("/materials/{materialId:int}/colors/{colorId:int}", Name = "GetMaterialHasColor")]
    [RequirePermission(CatalogPermissions.MaterialsRead)]
    public async Task<ActionResult<MaterialColorResponse>> GetColorsForMaterialAsync(int materialId, int colorId, CancellationToken cancellationToken)
    {
        var link = await service.GetMaterialColorAsync(materialId, colorId, cancellationToken);
        return link is null ? NotFound() : link;
    }

    /// <summary>Returns colors linked to a material.</summary>
    [HttpGet("/materials/{id:int}/colors")]
    [RequirePermission(CatalogPermissions.MaterialsRead)]
    public async Task<ActionResult<IReadOnlyList<ColorResponse>>> GetMaterialColorsAsync(int id, CancellationToken cancellationToken)
    {
        var colors = await service.GetMaterialColorsAsync(id, cancellationToken);
        return colors is null || colors.Count == 0 ? NotFound() : Ok(colors);
    }

    /// <summary>Updates a color.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(CatalogPermissions.ColorsUpdate)]
    public async Task<ActionResult> UpdateColorAsync(int id, UpsertColorRequest request, CancellationToken cancellationToken) =>
        await service.UpdateColorAsync(id, request, cancellationToken) ? NoContent() : NotFound();
}
