using Legacy.Maliev.CatalogService.Api.Authorization;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CatalogService.Api.Controllers;

/// <summary>Preserves the legacy Countries HTTP contract.</summary>
[ApiController]
[Route("[controller]")]
[Authorize]
public sealed class CountriesController(ICatalogService service) : ControllerBase
{
    /// <summary>Creates a country.</summary>
    [HttpPost]
    [RequirePermission(CatalogPermissions.CountriesCreate)]
    public async Task<ActionResult> CreateCountryAsync(UpsertCountryRequest request, CancellationToken cancellationToken)
    {
        var created = await service.CreateCountryAsync(request, cancellationToken);
        return CreatedAtRoute("GetCountry", new { id = created.Id }, created);
    }

    /// <summary>Deletes a country.</summary>
    [HttpDelete("{id}")]
    [RequirePermission(CatalogPermissions.CountriesDelete)]
    public async Task<ActionResult> DeleteCountryAsync(int id, CancellationToken cancellationToken) =>
        await service.DeleteCountryAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Returns countries ordered by name.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<CountryResponse>>> GetAllCountriesAsync(CancellationToken cancellationToken)
    {
        var countries = await service.GetCountriesAsync(cancellationToken);
        return countries.Count == 0 ? NotFound() : Ok(countries);
    }

    /// <summary>Returns a country.</summary>
    [HttpGet("{id}", Name = "GetCountry")]
    [RequirePermission(CatalogPermissions.CountriesRead)]
    public async Task<ActionResult<CountryResponse>> GetCountryAsync(int id, CancellationToken cancellationToken)
    {
        var country = await service.GetCountryAsync(id, cancellationToken);
        return country is null ? NotFound() : country;
    }

    /// <summary>Updates a country.</summary>
    [HttpPut("{id}")]
    [RequirePermission(CatalogPermissions.CountriesUpdate)]
    public async Task<ActionResult> UpdateCountryAsync(int id, UpsertCountryRequest request, CancellationToken cancellationToken) =>
        id == 0 ? BadRequest() : await service.UpdateCountryAsync(id, request, cancellationToken) ? NoContent() : NotFound();
}
