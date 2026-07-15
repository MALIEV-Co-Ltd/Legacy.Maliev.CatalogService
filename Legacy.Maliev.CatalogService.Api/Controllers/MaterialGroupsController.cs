using Legacy.Maliev.CatalogService.Api.Authorization;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CatalogService.Api.Controllers;

/// <summary>Preserves the legacy materials/MaterialGroups HTTP contract.</summary>
[ApiController]
[Route("materials/[controller]")]
[Authorize]
public sealed class MaterialGroupsController(ICatalogService service) : ControllerBase
{
    /// <summary>Creates a material group.</summary>
    [HttpPost]
    [RequirePermission(CatalogPermissions.MaterialGroupsCreate)]
    public async Task<ActionResult> CreateMaterialGroupAsync(UpsertMaterialGroupRequest request, CancellationToken cancellationToken)
    {
        var created = await service.CreateMaterialGroupAsync(request, cancellationToken);
        return CreatedAtRoute("GetMaterialGroup", new { id = created.Id }, created);
    }

    /// <summary>Deletes a material group.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(CatalogPermissions.MaterialGroupsDelete)]
    public async Task<ActionResult> DeleteMaterialGroupAsync(int id, CancellationToken cancellationToken) =>
        await service.DeleteMaterialGroupAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Returns all material groups.</summary>
    [HttpGet]
    [RequirePermission(CatalogPermissions.MaterialGroupsRead)]
    public async Task<ActionResult<IReadOnlyList<MaterialGroupResponse>>> GetMaterialGroupsAsync(CancellationToken cancellationToken)
    {
        var groups = await service.GetMaterialGroupsAsync(cancellationToken);
        return groups.Count == 0 ? NotFound() : Ok(groups);
    }

    /// <summary>Returns a material group.</summary>
    [HttpGet("{id:int}", Name = "GetMaterialGroup")]
    [RequirePermission(CatalogPermissions.MaterialGroupsRead)]
    public async Task<ActionResult<MaterialGroupResponse>> GetMaterialGroupAsync(int id, CancellationToken cancellationToken)
    {
        var group = await service.GetMaterialGroupAsync(id, cancellationToken);
        return group is null ? NotFound() : group;
    }

    /// <summary>Updates a material group.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(CatalogPermissions.MaterialGroupsUpdate)]
    public async Task<ActionResult> UpdateMaterialGroupAsync(int id, UpsertMaterialGroupRequest request, CancellationToken cancellationToken) =>
        await service.UpdateMaterialGroupAsync(id, request, cancellationToken) ? NoContent() : NotFound();
}
