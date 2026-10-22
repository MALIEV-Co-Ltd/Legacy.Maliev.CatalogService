using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OriginalCurrency = global::Maliev.CurrencyService.Data.Database.CurrencyContext.Currency;

namespace Legacy.Maliev.CatalogService.Tests.SourceCompatibility;

// Public MVC 10.0.3 reconstruction of the initial/source MVC 8.0.3 formatter seam.
// Later retained source uses MVC 8.0.28; the compared formatter/settings/pool match both.
// This is not execution of the private original application or its infrastructure.
public sealed class CurrencyAdmissionSourceHost : IDisposable
{
    private readonly IHost _host;
    public HttpClient Client { get; }

    public CurrencyAdmissionSourceHost()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddControllers()
                        .ConfigureApplicationPartManager(manager =>
                        {
                            manager.ApplicationParts.Clear();
                            manager.FeatureProviders.Clear();
                            manager.FeatureProviders.Add(new OnlyCurrencyOracle());
                        })
                        .AddNewtonsoftJson(options =>
                        {
                            options.SerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
                            options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
                        });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .Build();
        try
        {
            _host.Start();
            Client = _host.GetTestClient();
        }
        catch (Exception startupFailure)
        {
            try
            {
                _host.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(startupFailure, cleanupFailure);
            }
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            Client.Dispose();
        }
        finally
        {
            _host.Dispose();
        }
    }

    private sealed class OnlyCurrencyOracle : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) =>
            feature.Controllers.Add(typeof(CurrencyAdmissionSourceController).GetTypeInfo());
    }
}

[ApiController]
[Route("currency-source-oracle")]
public sealed class CurrencyAdmissionSourceController : ControllerBase
{
    [HttpPost]
    public IActionResult Read([FromBody] OriginalCurrency? item) => item is null
        ? BadRequest()
        : Content(JsonSerializer.Serialize(item), "application/json");
}
