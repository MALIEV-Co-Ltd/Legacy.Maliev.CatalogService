using Legacy.Maliev.CatalogService.Api.Authorization;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CatalogService.Api.Controllers;

/// <summary>Preserves the legacy Currencies HTTP contract.</summary>
[ApiController]
[Route("[controller]")]
[Authorize]
public sealed class CurrenciesController(ICatalogService service) : ControllerBase
{
    /// <summary>Creates a currency.</summary>
    [HttpPost]
    [RequirePermission(CatalogPermissions.CurrenciesCreate)]
    public async Task<ActionResult> CreateCurrencyAsync(UpsertCurrencyRequest request, CancellationToken cancellationToken)
    {
        var created = await service.CreateCurrencyAsync(request, cancellationToken);
        return CreatedAtRoute("GetCurrency", new { id = created.Id }, created);
    }

    /// <summary>Deletes a currency.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(CatalogPermissions.CurrenciesDelete)]
    public async Task<ActionResult> DeleteCurrencyAsync(int id, CancellationToken cancellationToken) =>
        await service.DeleteCurrencyAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Returns all currencies.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<CurrencyResponse>>> GetAllCurrenciesAsync(CancellationToken cancellationToken)
    {
        var currencies = await service.GetCurrenciesAsync(cancellationToken);
        return currencies.Count == 0 ? NotFound() : Ok(currencies);
    }

    /// <summary>Returns a currency.</summary>
    [HttpGet("{id:int}", Name = "GetCurrency")]
    [RequirePermission(CatalogPermissions.CurrenciesRead)]
    public async Task<ActionResult<CurrencyResponse>> GetCurrencyAsync(int id, CancellationToken cancellationToken)
    {
        var currency = await service.GetCurrencyAsync(id, cancellationToken);
        return currency is null ? NotFound() : currency;
    }

    /// <summary>Updates a currency.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(CatalogPermissions.CurrenciesUpdate)]
    public async Task<ActionResult> UpdateCurrencyAsync(int id, UpsertCurrencyRequest request, CancellationToken cancellationToken) =>
        id == 0 ? BadRequest() : await service.UpdateCurrencyAsync(id, request, cancellationToken) ? NoContent() : NotFound();
}
