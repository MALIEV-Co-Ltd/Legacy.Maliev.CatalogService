using System.Net;
using System.Text;
using Legacy.Maliev.CatalogService.Data;

namespace Legacy.Maliev.CatalogService.Tests.Data;

public sealed class FrankfurterExchangeRateClientTests
{
    [Fact]
    public async Task GetLatestAsync_LowercaseCurrencies_ReturnsLegacyStringRateShape()
    {
        Uri? requestedUri = null;
        var handler = new StubHandler(request =>
        {
            requestedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"base":"THB","date":"2026-07-15","rates":{"USD":0.03081}}""", Encoding.UTF8, "application/json"),
            };
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.app/") };
        var exchangeRates = new FrankfurterExchangeRateClient(client);

        var result = await exchangeRates.GetLatestAsync(" thb ", "usd", CancellationToken.None);

        Assert.Equal("https://api.frankfurter.app/latest?amount=1&from=THB&to=USD", requestedUri?.AbsoluteUri);
        Assert.Equal("THB", result.Base);
        Assert.Equal("0.03081", result.Rates["USD"]);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
