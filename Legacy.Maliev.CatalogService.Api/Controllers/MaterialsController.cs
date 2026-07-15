using Legacy.Maliev.CatalogService.Api.Authorization;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CatalogService.Api.Controllers;

/// <summary>Preserves the legacy Materials HTTP contract.</summary>
[ApiController]
[Route("[controller]")]
[Authorize]
public sealed class MaterialsController(ICatalogService service) : ControllerBase
{
    /// <summary>Creates a material.</summary>
    [HttpPost]
    [RequirePermission(CatalogPermissions.MaterialsCreate)]
    public async Task<ActionResult> CreateMaterialAsync(UpsertMaterialRequest request, CancellationToken cancellationToken)
    {
        var created = await service.CreateMaterialAsync(request, cancellationToken);
        return CreatedAtRoute("GetMaterial", new { id = created.Id }, created);
    }

    /// <summary>Creates a supplier link for a material.</summary>
    [HttpPost("{materialId:int}/suppliers/{supplierId:int}")]
    [RequirePermission(CatalogPermissions.MaterialsUpdate)]
    public async Task<ActionResult> CreateMaterialSupplierLinkAsync(int materialId, int supplierId, CancellationToken cancellationToken)
    {
        if (materialId == 0 || supplierId == 0)
        {
            return BadRequest();
        }

        var created = await service.CreateMaterialSupplierAsync(materialId, supplierId, cancellationToken);
        if (created is null)
        {
            return NotFound();
        }

        var material = await service.GetMaterialAsync(materialId, cancellationToken);
        return CreatedAtRoute("GetMaterialSupplier", new { id = materialId }, material);
    }

    /// <summary>Deletes a material and its association records.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(CatalogPermissions.MaterialsDelete)]
    public async Task<ActionResult> DeleteMaterialAsync(int id, CancellationToken cancellationToken) =>
        await service.DeleteMaterialAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Returns machinable materials.</summary>
    [HttpGet("machinable")]
    [RequirePermission(CatalogPermissions.MaterialsRead)]
    public async Task<ActionResult<IReadOnlyList<MaterialResponse>>> GetMachinableMaterialsAsync(CancellationToken cancellationToken)
    {
        var materials = await service.GetMachinableMaterialsAsync(cancellationToken);
        return materials.Count == 0 ? NotFound() : Ok(materials);
    }

    /// <summary>Returns a material.</summary>
    [HttpGet("{id:int}", Name = "GetMaterial")]
    [RequirePermission(CatalogPermissions.MaterialsRead)]
    public async Task<ActionResult<MaterialResponse>> GetMaterialAsync(int id, CancellationToken cancellationToken)
    {
        var material = await service.GetMaterialAsync(id, cancellationToken);
        return material is null ? NotFound() : material;
    }

    /// <summary>Returns supplier links for a material.</summary>
    [HttpGet("{id:int}/suppliers", Name = "GetMaterialSupplier")]
    [RequirePermission(CatalogPermissions.MaterialsRead)]
    public async Task<ActionResult<IReadOnlyList<MaterialSupplierResponse>>> GetMaterialSupplierAsync(int id, CancellationToken cancellationToken)
    {
        var suppliers = await service.GetMaterialSuppliersAsync(id, cancellationToken);
        return suppliers.Count == 0 ? NotFound() : Ok(suppliers);
    }

    /// <summary>Returns paginated materials using the legacy query contract.</summary>
    [HttpGet]
    [RequirePermission(CatalogPermissions.MaterialsRead)]
    public async Task<ActionResult<PaginatedMaterialResponse>> GetPaginatedMaterialAsync(
        [FromQuery] MaterialSortType? sort,
        [FromQuery] string? search,
        [FromQuery] int? index,
        [FromQuery] int? size,
        CancellationToken cancellationToken)
    {
        var materials = await service.GetMaterialsAsync(sort, search, index, size, cancellationToken);
        return materials.Items.Count == 0 ? NotFound() : materials;
    }

    /// <summary>Returns printable materials.</summary>
    [HttpGet("printable")]
    [RequirePermission(CatalogPermissions.MaterialsRead)]
    public async Task<ActionResult<IReadOnlyList<MaterialResponse>>> GetPrintableMaterialsAsync(CancellationToken cancellationToken)
    {
        var materials = await service.GetPrintableMaterialsAsync(cancellationToken);
        return materials.Count == 0 ? NotFound() : Ok(materials);
    }

    /// <summary>Updates a material.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(CatalogPermissions.MaterialsUpdate)]
    public async Task<ActionResult> UpdateMaterialAsync(int id, UpsertMaterialRequest request, CancellationToken cancellationToken) =>
        await service.UpdateMaterialAsync(id, request, cancellationToken) ? NoContent() : NotFound();
}
