using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class CatalogExchangeRateHttpTests : IAsyncLifetime
{
    private readonly RSA signingKey = RSA.Create(2048);
    private WebApplication provider = null!;
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient originalClient = null!;
    private int calls;
    private int providerStatus = 200;
    private bool failFirstRequest;
    private string providerBody = """{"base":"THB","date":"2026-10-03","rates":{"USD":0.03081001}}""";
    private string? query;

    public async Task InitializeAsync()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MALIEV_OBSERVABILITY_READ_ONLY_STARTUP"), "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The FX fixture refuses ambient database-validation startup mode before creating its provider.");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<Func<string>>(_ => () => providerBody);
        builder.Services.AddControllers().AddNewtonsoftJson(options =>
        {
            options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
            options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
        })
            .ConfigureApplicationPartManager(manager =>
            {
                manager.FeatureProviders.Clear();
                manager.FeatureProviders.Add(new OriginalControllerFeatureProvider());
            });
        provider = builder.Build();
        provider.MapControllers();
        provider.MapGet("/latest", async context =>
        {
            var attempt = Interlocked.Increment(ref calls);
            query = context.Request.QueryString.Value;
            context.Response.StatusCode = failFirstRequest && attempt == 1 ? 503 : providerStatus;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(providerBody, context.RequestAborted);
        });
        await provider.StartAsync();
        var address = provider.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        originalClient = new HttpClient { BaseAddress = new Uri(address) };
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            foreach (var setting in new Dictionary<string, string>
            {
                ["ConnectionStrings:CatalogDbContext"] = "Host=127.0.0.1;Port=1;Database=unused;Username=fixture;Pooling=false",
                ["ConnectionStrings:CountryDbContext"] = "Host=127.0.0.1;Port=1;Database=unused;Username=fixture;Pooling=false",
                ["ConnectionStrings:CurrencyDbContext"] = "Host=127.0.0.1;Port=1;Database=unused;Username=fixture;Pooling=false",
                ["InstantQuotationCatalog:ReconciliationEnabled"] = "false",
                ["Cache:RedisEnabled"] = "false",
                ["CORS:AllowedOrigins:0"] = "https://example.test",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(signingKey.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = "catalog-exchange-fixture",
                ["Jwt:Audience"] = "catalog-exchange-fixture",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            }) host.UseSetting(setting.Key, setting.Value);
            // Override only the external provider address; retain the real typed client and resilience pipeline.
            host.ConfigureServices(services => services.AddHttpClient<IExchangeRateClient, FrankfurterExchangeRateClient>(client => client.BaseAddress = new Uri(address)));
        });
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("legacy-catalog.currencies.create", HttpStatusCode.Forbidden)]
    [InlineData("legacy-catalog.materials.read", HttpStatusCode.Forbidden)]
    public async Task Route_RequiresCurrencyReadPermissionBeforeProviderAccess(string? permission, HttpStatusCode expected)
    {
        using var client = Client(permission);
        using var response = await client.GetAsync("/currencies/exchangerates?baseCurrency=THB&targetCurrency=USD");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Route_ExactGrant_BindsQueriesAndReturnsLegacyStringRateShape()
    {
        using var client = Client("legacy-catalog.currencies.read");
        using var response = await client.GetAsync("/currencies/exchangerates?baseCurrency=%20thb%20&targetCurrency=usd");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal("?amount=1&from=%20thb%20&to=usd", query);
        using var wire = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("THB", wire.RootElement.GetProperty("Base").GetString());
        Assert.Equal(new DateTime(2026, 10, 3), wire.RootElement.GetProperty("Date").GetDateTime());
        Assert.Equal("0.03081001", wire.RootElement.GetProperty("Rates").GetProperty("USD").GetString());
        Assert.False(wire.RootElement.TryGetProperty("base", out _));
    }

    [Theory]
    [InlineData("baseCurrency=THB")]
    [InlineData("targetCurrency=USD")]
    [InlineData("baseCurrency=&targetCurrency=USD")]
    public async Task Route_MissingRequiredQuery_RejectsBeforeProvider(string parameters)
    {
        using var client = Client("legacy-catalog.currencies.read");
        using var response = await client.GetAsync($"/currencies/exchangerates?{parameters}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Route_TransientProviderFailure_UsesRegisteredReadResiliencePipeline()
    {
        failFirstRequest = true;
        using var client = Client("legacy-catalog.currencies.read");
        using var response = await client.GetAsync("/currencies/exchangerates?baseCurrency=THB&targetCurrency=USD");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, calls);
        using var wire = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("0.03081001", wire.RootElement.GetProperty("Rates").GetProperty("USD").GetString());
    }

    [Theory]
    [InlineData(400, "provider-internal-sentinel")]
    [InlineData(200, "{malformed-provider-internal-sentinel")]
    [InlineData(200, "{\"rates\":{\"provider-internal-sentinel\":{}},\"base\":\"THB\",\"date\":\"2026-10-03\"}")]
    [InlineData(200, "{\"rates\":{\"provider-internal-sentinel\":[]},\"base\":\"THB\",\"date\":\"2026-10-03\"}")]
    public async Task Route_ProviderFailure_DoesNotReturnSuccessOrLeakProviderBody(int status, string body)
    {
        providerStatus = status;
        providerBody = body;
        using var client = Client("legacy-catalog.currencies.read");
        using var response = await client.GetAsync("/currencies/exchangerates?baseCurrency=THB&targetCurrency=USD");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(calls >= 1);
        Assert.DoesNotContain("provider-internal-sentinel", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("thb", "usd", "?amount=1&from=thb&to=usd")]
    [InlineData(" thb ", " usd ", "?amount=1&from=%20thb%20&to=%20usd%20")]
    [InlineData("tHb", "uSd", "?amount=1&from=tHb&to=uSd")]
    [InlineData("THB", "USD", "?amount=1&from=THB&to=USD")]
    public async Task Route_SourceCurrencyLiterals_ReachRealProviderWithoutNormalization(string from, string to, string expectedQuery)
    {
        using var client = Client("legacy-catalog.currencies.read");
        using var response = await client.GetAsync($"/currencies/exchangerates?baseCurrency={Uri.EscapeDataString(from)}&targetCurrency={Uri.EscapeDataString(to)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal(expectedQuery, query);
        using var wire = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("THB", wire.RootElement.GetProperty("Base").GetString());
        Assert.Equal("0.03081001", wire.RootElement.GetProperty("Rates").GetProperty("USD").GetString());
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
    public async Task Route_StandardJsonRateScalars_PreserveOriginalStringsOnTheWire(string token, string? expected)
    {
        var original = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string?>>("{\"USD\":" + token + "}")!;
        Assert.Equal(expected, original["USD"]);
        providerBody = "{\"base\":\"THB\",\"date\":\"2026-10-03\",\"rates\":{\"USD\":" + token + "}}";
        using var client = Client("legacy-catalog.currencies.read");

        using var response = await client.GetAsync("/currencies/exchangerates?baseCurrency=THB&targetCurrency=USD");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal("?amount=1&from=THB&to=USD", query);
        using var wire = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("THB", wire.RootElement.GetProperty("Base").GetString());
        Assert.Equal(new DateTime(2026, 10, 3), wire.RootElement.GetProperty("Date").GetDateTime());
        var rates = wire.RootElement.GetProperty("Rates");
        Assert.Single(rates.EnumerateObject());
        var rate = rates.GetProperty("USD");
        Assert.Equal(expected is null ? JsonValueKind.Null : JsonValueKind.String, rate.ValueKind);
        Assert.Equal(expected, rate.GetString());
        Assert.False(wire.RootElement.TryGetProperty("rates", out _));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "THB")]
    [InlineData(true, "USD")]
    public async Task Route_ProviderBaseIdentity_PreservesLiteralOrSourceNullOmission(bool includeBase, string? providerBase)
    {
        var baseMember = includeBase ? "\"base\":" + JsonSerializer.Serialize(providerBase) + "," : string.Empty;
        providerBody = "{" + baseMember + "\"date\":\"2026-10-03\",\"rates\":{\"USD\":0.03081001}}";
        using var original = JsonDocument.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(
            Newtonsoft.Json.JsonConvert.DeserializeObject<OriginalOpenRatesModel>(providerBody),
            new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore }));
        using var client = Client("legacy-catalog.currencies.read");

        using var response = await client.GetAsync("/currencies/exchangerates?baseCurrency=THB&targetCurrency=USD");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal("?amount=1&from=THB&to=USD", query);
        using var wire = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var originalHasBase = original.RootElement.TryGetProperty("Base", out var originalBase);
        Assert.Equal(providerBase is not null, originalHasBase);
        Assert.Equal(originalHasBase, wire.RootElement.TryGetProperty("Base", out var actualBase));
        if (originalHasBase) Assert.Equal(originalBase.GetString(), actualBase.GetString());
        Assert.Equal(original.RootElement.GetProperty("Date").GetDateTime(), wire.RootElement.GetProperty("Date").GetDateTime());
        Assert.Equal(original.RootElement.GetProperty("Rates").GetProperty("USD").GetString(),
            wire.RootElement.GetProperty("Rates").GetProperty("USD").GetString());
        Assert.False(wire.RootElement.TryGetProperty("base", out _));
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
    public async Task Route_OriginalProviderParserMatrix_MatchesSourceMvcBoundary(string caseName, string body)
    {
        Assert.False(string.IsNullOrWhiteSpace(caseName));
        providerBody = body;
        using var client = Client("legacy-catalog.currencies.read");
        using var originalResponse = await originalClient.GetAsync("/original/exchangerates");
        using var response = await client.GetAsync("/currencies/exchangerates?baseCurrency=THB&targetCurrency=USD");

        Assert.Equal(originalResponse.StatusCode, response.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal("?amount=1&from=THB&to=USD", query);
        var actualBody = await response.Content.ReadAsStringAsync();
        if (originalResponse.StatusCode == HttpStatusCode.NoContent)
        {
            Assert.Equal(string.Empty, actualBody);
            return;
        }
        if (originalResponse.StatusCode != HttpStatusCode.OK)
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain("provider-internal-sentinel", actualBody, StringComparison.Ordinal);
            return;
        }
        using var originalWire = JsonDocument.Parse(await originalResponse.Content.ReadAsStringAsync());
        using var wire = JsonDocument.Parse(actualBody);
        // Catalog's accepted PascalCase consumer contract is retained explicitly.
        static string CatalogName(string sourceName) => sourceName switch
        {
            "base" or "Base" => "Base",
            "date" or "Date" => "Date",
            "rates" or "Rates" => "Rates",
            _ => throw new InvalidOperationException("Unexpected original model property."),
        };
        Assert.Equal(originalWire.RootElement.EnumerateObject().Select(property => CatalogName(property.Name)),
            wire.RootElement.EnumerateObject().Select(property => property.Name));
        foreach (var property in originalWire.RootElement.EnumerateObject())
        {
            var actual = wire.RootElement.GetProperty(CatalogName(property.Name));
            if (CatalogName(property.Name) == "Date")
                Assert.Equal(property.Value.GetDateTime(), actual.GetDateTime());
            else
                Assert.Equal(property.Value.GetRawText(), actual.GetRawText());
        }
    }

    // This isolated loopback oracle executes the retained ActionResult<T> and Newtonsoft MVC settings.
    public sealed class OriginalExchangeRatesController(Func<string> body) : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        [Microsoft.AspNetCore.Mvc.HttpGet("/original/exchangerates")]
        public Task<Microsoft.AspNetCore.Mvc.ActionResult<OriginalOpenRatesModel>> GetLiveExchangeRatesAsync() =>
            Task.FromResult<Microsoft.AspNetCore.Mvc.ActionResult<OriginalOpenRatesModel>>(
                Newtonsoft.Json.JsonConvert.DeserializeObject<OriginalOpenRatesModel>(body())!);
    }

    private sealed class OriginalControllerFeatureProvider :
        Microsoft.AspNetCore.Mvc.ApplicationParts.IApplicationFeatureProvider<Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPart> parts,
            Microsoft.AspNetCore.Mvc.Controllers.ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(System.Reflection.IntrospectionExtensions.GetTypeInfo(typeof(OriginalExchangeRatesController)));
        }
    }

    public sealed class OriginalOpenRatesModel
    {
        public string? Base { get; set; }
        public DateTime Date { get; set; }
        public Dictionary<string, string>? Rates { get; set; }
    }

    private HttpClient Client(string? permission)
    {
        var client = factory.CreateClient();
        if (permission is null) return client;
        var token = new JwtSecurityToken("catalog-exchange-fixture", "catalog-exchange-fixture",
            [new Claim(JwtRegisteredClaimNames.Sub, "employee:currency-acceptance"), new Claim("permission", permission)],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(signingKey), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    public async Task DisposeAsync()
    {
        originalClient?.Dispose();
        try { if (factory is not null) await factory.DisposeAsync(); }
        finally
        {
            try { if (provider is not null) await provider.DisposeAsync(); }
            finally { signingKey.Dispose(); }
        }
    }
}
