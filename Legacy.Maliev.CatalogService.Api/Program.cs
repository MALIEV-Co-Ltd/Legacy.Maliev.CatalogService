using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Legacy.Maliev.CatalogService.Application.Lookups;
using Legacy.Maliev.CatalogService.Api.Lookups;
using Legacy.Maliev.CatalogService.Data.Lookups;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Distributed;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Services;
using Legacy.Maliev.CatalogService.Data;
using Legacy.Maliev.CatalogService.Api;
using Maliev.Aspire.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDefaultApiVersioning();
builder.AddPostgresDbContext<CatalogDbContext>(connectionName: "CatalogDbContext");
builder.AddPostgresDbContext<CatalogCountryDbContext>(connectionName: "CountryDbContext");
builder.AddPostgresDbContext<CatalogCurrencyDbContext>(connectionName: "CurrencyDbContext");
builder.AddStandardCache("legacy:catalog:");
builder.AddStandardCors();
builder.AddJwtAuthentication();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.AddStandardOpenApi(
    title: "Legacy MALIEV Catalog Service API",
    description: "Temporary .NET 10 compatibility service preserving legacy country, currency, and material API contracts.");
// The application-owned literal call activates its generated XML documentation transformers.
builder.Services.AddOpenApi("v1");
builder.Services.Configure<Microsoft.AspNetCore.OpenApi.OpenApiOptions>("v1", options =>
{
    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonPropertyInfo?.CustomConverter is Legacy.Maliev.CatalogService.Application.Models.LegacyScalarStringJsonConverter)
            schema.Type = Microsoft.OpenApi.JsonSchemaType.String;
        return Task.CompletedTask;
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.DictionaryKeyPolicy = null;
});

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.JsonSerializerOptions.PropertyNamingPolicy = null;
    options.JsonSerializerOptions.DictionaryKeyPolicy = null;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => ThaiAddressDataset.Load(builder.Environment.ContentRootPath));
var credenOptions = builder.Configuration.GetSection("Creden").Get<CredenOptions>() ?? new CredenOptions();
builder.Services.AddHttpClient("catalog-creden", client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<ICompanyLookup>(services => new CredenCompanyLookup(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("catalog-creden"),
    services.GetRequiredService<IDistributedCache>(), credenOptions, services.GetRequiredService<TimeProvider>()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("catalog-lookups", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("user_id")?.Value ?? context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.User.Identity?.Name ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddHttpClient<IExchangeRateClient, FrankfurterExchangeRateClient>(client =>
{
    client.BaseAddress = new Uri("https://api.frankfurter.app/");
    client.Timeout = TimeSpan.FromSeconds(15);
}).AddLegacyStandardResilienceHandler();
builder.Services.AddScoped<ICatalogRepository, CatalogRepository>();
builder.Services.AddScoped<ICatalogCache, DistributedCatalogCache>();
builder.Services.AddScoped<ICatalogService, CatalogApplicationService>();
builder.Services.AddScoped<InstantQuotationCatalogReconciler>();
builder.Services.AddHostedService<InstantQuotationCatalogStartupService>();

var app = builder.Build();

app.UseStandardMiddleware();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapDefaultEndpoints("catalog");
app.MapControllers();
app.MapApiDocumentation(servicePrefix: "catalog");

await app.RunAsync();

/// <summary>Legacy Catalog Service entry point.</summary>
public partial class Program;
