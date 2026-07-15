using Legacy.Maliev.CatalogService.Application.Models;

namespace Legacy.Maliev.CatalogService.Application.Interfaces;

/// <summary>Reads exchange rates from the external public provider.</summary>
public interface IExchangeRateClient
{
    /// <summary>Returns the latest rate between two ISO currencies.</summary>
    Task<ExchangeRateResponse> GetLatestAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken);
}
