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

        Assert.Equal("https://api.frankfurter.app/latest?amount=1&from=%20thb%20&to=usd", requestedUri?.AbsoluteUri);
        Assert.NotNull(result);
        Assert.NotNull(result.Rates);
        Assert.Equal("THB", result.Base);
        Assert.Equal("0.03081", result.Rates["USD"]);
    }

    [Theory]
    [InlineData("0.123456789012345678901234567890123456789", "0.123456789012345678901234567890123456789")]
    [InlineData("79228162514264337593543950336", "79228162514264337593543950336")]
    [InlineData("1e+03", "1e+03")]
    [InlineData("1e309", "1e309")]
    [InlineData("1e-400", "1e-400")]
    [InlineData("-0", "-0")]
    [InlineData("\"0.030810010000000000000000000000000001\"", "0.030810010000000000000000000000000001")]
    [InlineData("\" 0.03081 \"", " 0.03081 ")]
    [InlineData("true", "true")]
    [InlineData("null", null)]
    public async Task GetLatestAsync_StandardJsonRateScalars_PreserveOriginalStringContract(string token, string? expected)
    {
        var original = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string?>>("{\"USD\":" + token + "}")!;
        Assert.Equal(expected, original["USD"]);
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"base\":\"THB\",\"date\":\"2026-10-03\",\"rates\":{\"USD\":" + token + "}}", Encoding.UTF8, "application/json"),
        }))
        { BaseAddress = new Uri("https://api.frankfurter.app/") };

        var result = await new FrankfurterExchangeRateClient(client).GetLatestAsync("THB", "USD", CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Rates);
        Assert.Equal("THB", result.Base);
        Assert.Equal(new DateTime(2026, 10, 3), result.Date);
        Assert.Equal(expected, result.Rates["USD"]);
        Assert.Single(result.Rates);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task GetLatestAsync_StructuredRateValues_StillFailOriginalStringContract(string token)
    {
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string?>>("{\"USD\":" + token + "}"));
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"base\":\"THB\",\"date\":\"2026-10-03\",\"rates\":{\"USD\":" + token + "}}", Encoding.UTF8, "application/json"),
        }))
        { BaseAddress = new Uri("https://api.frankfurter.app/") };

        await Assert.ThrowsAnyAsync<Newtonsoft.Json.JsonException>(() =>
            new FrankfurterExchangeRateClient(client).GetLatestAsync("THB", "USD", CancellationToken.None));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("th-TH")]
    public async Task GetLatestAsync_NonEnglishCulture_PreservesOriginalRateLiterals(string culture)
    {
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            const string rates = "{\"USD\":0.123456789012345678901234567890123456789,\"EUR\":1e+03,\"JPY\":\" 4.50 \"}";
            var original = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(rates)!;
            Assert.Equal("0.123456789012345678901234567890123456789", original["USD"]);
            Assert.Equal("1e+03", original["EUR"]);
            Assert.Equal(" 4.50 ", original["JPY"]);
            using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"base\":\"THB\",\"date\":\"2026-10-03\",\"rates\":" + rates + "}", Encoding.UTF8, "application/json"),
            }))
            { BaseAddress = new Uri("https://api.frankfurter.app/") };

            var result = await new FrankfurterExchangeRateClient(client).GetLatestAsync("THB", "USD", CancellationToken.None);

            Assert.NotNull(result);
            Assert.NotNull(result.Rates);
            Assert.Equal(original.OrderBy(pair => pair.Key), result.Rates.OrderBy(pair => pair.Key));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "THB")]
    [InlineData(true, "USD")]
    public async Task GetLatestAsync_ProviderBaseIdentity_IsNeverInventedFromTheRequest(bool includeBase, string? providerBase)
    {
        var baseMember = includeBase ? "\"base\":" + System.Text.Json.JsonSerializer.Serialize(providerBase) + "," : string.Empty;
        var body = "{" + baseMember + "\"date\":\"2026-10-03\",\"rates\":{\"USD\":0.03081001}}";
        var original = Newtonsoft.Json.JsonConvert.DeserializeObject<OriginalOpenRatesModel>(body)!;
        Assert.Equal(providerBase, original.Base);
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }))
        { BaseAddress = new Uri("https://api.frankfurter.app/") };

        var result = await new FrankfurterExchangeRateClient(client).GetLatestAsync("THB", "USD", CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Rates);
        Assert.Equal(original.Base, result.Base);
        Assert.Equal(original.Date, result.Date);
        Assert.NotNull(original.Rates);
        Assert.Equal(original.Rates["USD"], result.Rates["USD"]);
    }


    [Theory]
    [InlineData("missing-all", "{}")]
    [InlineData("missing-date", "{\"base\":\"THB\",\"rates\":{\"USD\":1}}")]
    [InlineData("null-date", "{\"base\":\"THB\",\"date\":null,\"rates\":{\"USD\":1}}")]
    [InlineData("empty-date", "{\"base\":\"THB\",\"date\":\"\",\"rates\":{\"USD\":1}}")]
    [InlineData("valid-date", "{\"base\":\"THB\",\"date\":\"2026-10-03\",\"rates\":{\"USD\":1}}")]
    [InlineData("timestamp-date", "{\"base\":\"THB\",\"date\":\"2026-10-03T12:34:56Z\",\"rates\":{\"USD\":1}}")]
    [InlineData("offset-date", "{\"base\":\"THB\",\"date\":\"2026-10-03T12:34:56+07:00\",\"rates\":{\"USD\":1}}")]
    [InlineData("microsoft-date", "{\"base\":\"THB\",\"date\":\"/Date(1790985600000)/\",\"rates\":{\"USD\":1}}")]
    [InlineData("invalid-date", "{\"base\":\"THB\",\"date\":\"provider-internal-sentinel\",\"rates\":{\"USD\":1}}")]
    [InlineData("number-date", "{\"base\":\"THB\",\"date\":123,\"rates\":{\"USD\":1}}")]
    [InlineData("missing-rates", "{\"base\":\"THB\",\"date\":\"2026-10-03\"}")]
    [InlineData("null-rates", "{\"base\":\"THB\",\"date\":\"2026-10-03\",\"rates\":null}")]
    [InlineData("empty-rates", "{\"base\":\"THB\",\"date\":\"2026-10-03\",\"rates\":{}}")]
    [InlineData("array-rates", "{\"base\":\"THB\",\"rates\":[]}")]
    [InlineData("string-rates", "{\"base\":\"THB\",\"rates\":\"provider-internal-sentinel\"}")]
    [InlineData("root-null", "null")]
    [InlineData("root-array", "[]")]
    [InlineData("root-string", "\"provider-internal-sentinel\"")]
    [InlineData("upper-case", "{\"BASE\":\"THB\",\"DATE\":\"2026-10-03\",\"RATES\":{\"USD\":1}}")]
    [InlineData("mixed-case", "{\"bAsE\":\"THB\",\"dAtE\":\"2026-10-03\",\"rAtEs\":{\"usd\":1,\"USD\":2}}")]
    [InlineData("duplicate-base", "{\"base\":\"USD\",\"Base\":\"THB\",\"rates\":{\"USD\":1}}")]
    [InlineData("duplicate-date", "{\"date\":\"2026-10-02\",\"Date\":\"2026-10-03\",\"rates\":{\"USD\":1}}")]
    [InlineData("duplicate-rate-key", "{\"base\":\"THB\",\"rates\":{\"USD\":1,\"USD\":2}}")]
    [InlineData("duplicate-rates", "{\"base\":\"THB\",\"rates\":{\"USD\":1},\"Rates\":{\"EUR\":2}}")]
    [InlineData("duplicate-rates-null", "{\"base\":\"THB\",\"rates\":{\"USD\":1},\"rates\":null}")]
    [InlineData("unknown-member", "{\"base\":\"THB\",\"rates\":{\"USD\":1},\"unused\":{\"provider-internal-sentinel\":[]}}")]
    [InlineData("number-base", "{\"base\":123,\"rates\":{\"USD\":1}}")]
    [InlineData("boolean-base", "{\"base\":true,\"rates\":{\"USD\":1}}")]
    [InlineData("array-base", "{\"base\":[],\"rates\":{\"USD\":1}}")]
    [InlineData("object-base", "{\"base\":{},\"rates\":{\"USD\":1}}")]
    [InlineData("null-base", "{\"base\":null,\"rates\":{\"USD\":1}}")]
    [InlineData("all-null", "{\"base\":null,\"date\":null,\"rates\":null}")]
    [InlineData("malformed-json", "{provider-internal-sentinel")]
    [InlineData("trailing-comma", "{\"base\":\"THB\",\"rates\":{\"USD\":1},}")]
    [InlineData("comment-json", "{\"base\":\"THB\",/* provider-internal-sentinel */\"rates\":{\"USD\":1}}")]
    public async Task GetLatestAsync_OriginalProviderParserMatrix_PreservesStockModel(string caseName, string body)
    {
        Assert.False(string.IsNullOrWhiteSpace(caseName));
        OriginalOpenRatesModel? original = null;
        var originalFailure = Record.Exception(() =>
            original = Newtonsoft.Json.JsonConvert.DeserializeObject<OriginalOpenRatesModel>(body));
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }))
        { BaseAddress = new Uri("https://api.frankfurter.app/") };

        if (originalFailure is not null)
        {
            Assert.IsAssignableFrom<Newtonsoft.Json.JsonException>(originalFailure);
            await Assert.ThrowsAnyAsync<Newtonsoft.Json.JsonException>(() =>
                new FrankfurterExchangeRateClient(client).GetLatestAsync("THB", "USD", CancellationToken.None));
            return;
        }

        var result = await new FrankfurterExchangeRateClient(client).GetLatestAsync("THB", "USD", CancellationToken.None);

        if (original is null)
        {
            Assert.Null(result);
            return;
        }
        Assert.NotNull(result);
        Assert.Equal(original.Base, result.Base);
        Assert.Equal(original.Date, result.Date);
        Assert.Equal(original.Date.Kind, result.Date.Kind);
        if (original.Rates is null) Assert.Null(result.Rates);
        else
        {
            Assert.NotNull(result.Rates);
            Assert.Equal(original.Rates.OrderBy(pair => pair.Key), result.Rates.OrderBy(pair => pair.Key));
        }
    }

    // The three property types match the retained original OpenRatesModel.
    private sealed class OriginalOpenRatesModel
    {
        public string? Base { get; set; }
        public DateTime Date { get; set; }
        public Dictionary<string, string>? Rates { get; set; }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
