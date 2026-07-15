using Legacy.Maliev.CatalogService.Api.Authorization;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CatalogService.Api.Controllers;

/// <summary>Preserves the legacy currencies/exchangerates contract.</summary>
[ApiController]
[Route("[controller]")]
[Authorize]
public sealed class ExchangeRatesController(IExchangeRateClient exchangeRateClient) : ControllerBase
{
    /// <summary>Returns the latest exchange rate.</summary>
    [HttpGet("/currencies/exchangerates")]
    [RequirePermission(CatalogPermissions.CurrenciesRead)]
    public Task<ExchangeRateResponse> GetLiveExchangeRatesAsync(
        [FromQuery] string baseCurrency,
        [FromQuery] string targetCurrency,
        CancellationToken cancellationToken) =>
        exchangeRateClient.GetLatestAsync(baseCurrency, targetCurrency, cancellationToken);
}
