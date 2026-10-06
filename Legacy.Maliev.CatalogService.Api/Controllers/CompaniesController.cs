using Legacy.Maliev.CatalogService.Api.Authorization;
using Legacy.Maliev.CatalogService.Application.Lookups;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Legacy.Maliev.CatalogService.Api.Controllers;

/// <summary>Truthful bounded company suggestions with an operator-controlled provider switch.</summary>
[ApiController, Route("api/v1/companies"), Authorize, RequirePermission(CatalogPermissions.CompaniesRead), EnableRateLimiting("catalog-lookups")]
public sealed class CompaniesController(ICompanyLookup lookup) : ControllerBase
{
    /// <summary>Searches supported provider fields, or returns an explicit unavailable/unsupported outcome.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<CompanyLookup>> Search([FromQuery] string q, CancellationToken cancellationToken, [FromQuery] string queryType = "name", [FromQuery] string language = "th", [FromQuery] int limit = 20)
    {
        try
        {
            var result = await lookup.SearchAsync(q, queryType, language, limit, cancellationToken);
            if (result.Outcome == "rate-limited" && result.RetryAfterSeconds is { } retry)
                Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(result.Outcome switch
            {
                "rate-limited" => 429,
                "unavailable" => 503,
                "unsupported" => 422,
                _ => 200
            }, result);
        }
        catch (ArgumentException)
        {
            return Problem(statusCode: 400, title: "Invalid company lookup input.");
        }
    }
}
