using Legacy.Maliev.CatalogService.Api.Authorization;
using Legacy.Maliev.CatalogService.Application.Lookups;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Legacy.Maliev.CatalogService.Api.Controllers;

/// <summary>Versioned local Thai administrative lookup; all access requires service/staff permission.</summary>
[ApiController, Route("api/v1/thai-addresses"), Authorize, RequirePermission(CatalogPermissions.LocationsRead), EnableRateLimiting("catalog-lookups")]
public sealed class ThaiAddressesController(ThaiAddressLookup lookup) : ControllerBase
{
    /// <summary>Lists provinces matching all supplied constraints.</summary>
    [HttpGet("provinces")]
    public ActionResult<LookupPage<AdministrativeArea>> Provinces([FromQuery] AddressQuery query) => Execute(() => lookup.Areas("provinces", query));
    /// <summary>Lists districts matching parent and postcode constraints.</summary>
    [HttpGet("districts")]
    public ActionResult<LookupPage<AdministrativeArea>> Districts([FromQuery] AddressQuery query) => Execute(() => lookup.Areas("districts", query));
    /// <summary>Lists subdistricts matching parent and postcode constraints.</summary>
    [HttpGet("subdistricts")]
    public ActionResult<LookupPage<AdministrativeArea>> Subdistricts([FromQuery] AddressQuery query) => Execute(() => lookup.Areas("subdistricts", query));
    /// <summary>Lists postcodes without assuming a unique administrative combination.</summary>
    [HttpGet("postcodes")]
    public ActionResult<LookupPage<string>> Postcodes([FromQuery] AddressQuery query) => Execute(() => lookup.Postcodes(query));
    /// <summary>Returns AND-filtered bilingual administrative and postcode combinations.</summary>
    [HttpGet("autocomplete")]
    public ActionResult<LookupPage<AddressCombination>> Autocomplete([FromQuery] AddressQuery query) => Execute(() => lookup.Search(query));
    /// <summary>Preserves pasted text and extracts administrative components conservatively.</summary>
    [HttpPost("resolve"), RequestSizeLimit(16384)]
    public ActionResult<AddressResolution> Resolve(ResolveAddressRequest request) => Execute(() => lookup.Resolve(request));
    private ActionResult<T> Execute<T>(Func<T> operation)
    {
        try
        {
            return Ok(operation());
        }
        catch (ArgumentException)
        {
            return Problem(statusCode: 400, title: "Invalid address lookup input.");
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return Problem(statusCode: 503, title: "Thai address resolution unavailable.");
        }
        catch (InvalidOperationException)
        {
            return Problem(statusCode: 503, title: "Thai address dataset unavailable.");
        }
    }
}
