using System.Globalization;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Models;

namespace Legacy.Maliev.CatalogService.Data;

/// <summary>Resilient HTTP client for the public Frankfurter exchange-rate API.</summary>
public sealed class FrankfurterExchangeRateClient(HttpClient httpClient) : IExchangeRateClient
{
    /// <inheritdoc />
    public async Task<ExchangeRateResponse> GetLatestAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken)
    {
        var from = Uri.EscapeDataString(baseCurrency.Trim().ToUpperInvariant());
        var to = Uri.EscapeDataString(targetCurrency.Trim().ToUpperInvariant());
        using var response = await httpClient.GetAsync($"latest?amount=1&from={from}&to={to}", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var rates = root.GetProperty("rates").EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.GetDecimal().ToString(CultureInfo.InvariantCulture),
            StringComparer.Ordinal);
        return new(
            root.GetProperty("base").GetString() ?? from,
            root.GetProperty("date").GetDateTime(),
            rates);
    }
}
