using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Models;
using Newtonsoft.Json;

namespace Legacy.Maliev.CatalogService.Data;

/// <summary>Resilient HTTP client for the public Frankfurter exchange-rate API.</summary>
public sealed class FrankfurterExchangeRateClient(HttpClient httpClient) : IExchangeRateClient
{
    /// <inheritdoc />
    public async Task<ExchangeRateResponse?> GetLatestAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken)
    {
        var from = Uri.EscapeDataString(baseCurrency);
        var to = Uri.EscapeDataString(targetCurrency);
        using var response = await httpClient.GetAsync($"latest?amount=1&from={from}&to={to}", cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var original = JsonConvert.DeserializeObject<OriginalOpenRatesModel>(body);
        return original is null ? null : new(original.Base, original.Date, original.Rates);
    }

    // Preserve the original provider model and stock parser inside this adapter only.
    private sealed class OriginalOpenRatesModel
    {
        public string? Base { get; set; }
        public DateTime Date { get; set; }
        public Dictionary<string, string>? Rates { get; set; }
    }
}
