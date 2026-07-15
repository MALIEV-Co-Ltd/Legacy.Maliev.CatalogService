using System.Text.Json.Serialization;
using Legacy.Maliev.CatalogService.Application.Interfaces;
using Legacy.Maliev.CatalogService.Application.Services;
using Legacy.Maliev.CatalogService.Data;
using Maliev.Aspire.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDefaultApiVersioning();
builder.AddPostgresDbContext<CatalogDbContext>(connectionName: "CatalogDbContext");
builder.AddStandardCache("legacy:catalog:");
builder.AddStandardCors();
builder.AddJwtAuthentication();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.AddStandardOpenApi(
    title: "Legacy MALIEV Catalog Service API",
    description: "Temporary .NET 10 compatibility service preserving legacy country, currency, and material API contracts.");

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.JsonSerializerOptions.PropertyNamingPolicy = null;
    options.JsonSerializerOptions.DictionaryKeyPolicy = null;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IExchangeRateClient, FrankfurterExchangeRateClient>(client =>
{
    client.BaseAddress = new Uri("https://api.frankfurter.app/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped<ICatalogRepository, CatalogRepository>();
builder.Services.AddScoped<ICatalogCache, DistributedCatalogCache>();
builder.Services.AddScoped<ICatalogService, CatalogApplicationService>();

var app = builder.Build();

app.UseStandardMiddleware();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints("catalog");
app.MapControllers();
app.MapApiDocumentation(servicePrefix: "catalog");

await app.RunAsync();

/// <summary>Legacy Catalog Service entry point.</summary>
public partial class Program;
