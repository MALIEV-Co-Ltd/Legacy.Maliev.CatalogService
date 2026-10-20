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
        provider = builder.Build();
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

    private sealed class OriginalOpenRatesModel
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
        try { if (factory is not null) await factory.DisposeAsync(); }
        finally
        {
            try { if (provider is not null) await provider.DisposeAsync(); }
            finally { signingKey.Dispose(); }
        }
    }
}
