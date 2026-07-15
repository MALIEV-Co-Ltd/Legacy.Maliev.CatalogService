using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.CatalogService.Api.Controllers;
using Legacy.Maliev.CatalogService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Legacy.Maliev.CatalogService.Tests.Controllers;

public sealed class CatalogControllerContractTests
{
    public static TheoryData<Type, string> ControllerRoutes => new()
    {
        { typeof(CountriesController), "[controller]" },
        { typeof(CurrenciesController), "[controller]" },
        { typeof(ExchangeRatesController), "[controller]" },
        { typeof(MaterialsController), "[controller]" },
        { typeof(ColorsController), "materials/[controller]" },
        { typeof(MaterialGroupsController), "materials/[controller]" },
        { typeof(SurfaceFinishesController), "materials/[controller]" },
    };

    [Theory]
    [MemberData(nameof(ControllerRoutes))]
    public void Controllers_PreserveLegacyUnversionedRoutePrefixes(Type controllerType, string expectedTemplate)
    {
        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        Assert.Equal(expectedTemplate, route?.Template);
        Assert.NotNull(controllerType.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Theory]
    [InlineData(typeof(ExchangeRatesController), nameof(ExchangeRatesController.GetLiveExchangeRatesAsync), "/currencies/exchangerates")]
    [InlineData(typeof(ColorsController), nameof(ColorsController.CreateColorsForMaterialAsync), "/materials/{materialId:int}/colors/{colorId:int}")]
    [InlineData(typeof(ColorsController), nameof(ColorsController.GetMaterialColorsAsync), "/materials/{id:int}/colors")]
    [InlineData(typeof(SurfaceFinishesController), nameof(SurfaceFinishesController.CreateSurfaceFinishForMaterialAsync), "/materials/{materialId:int}/surfacefinishes/{surfaceFinishId:int}")]
    [InlineData(typeof(SurfaceFinishesController), nameof(SurfaceFinishesController.GetMaterialSurfaceFinishesAsync), "/materials/{id:int}/surfacefinishes")]
    public void AbsoluteRoutes_PreserveLegacyTemplates(Type controllerType, string methodName, string expectedTemplate)
    {
        var method = controllerType.GetMethod(methodName);
        var route = method?.GetCustomAttributes().OfType<IRouteTemplateProvider>().Single(attribute => attribute.Template?.StartsWith('/') == true);
        Assert.Equal(expectedTemplate, route?.Template);
    }

    [Fact]
    public void AnonymousAccess_RemainsLimitedToLegacyCountryAndCurrencyLists()
    {
        var anonymous = ControllerRoutes.SelectMany(row => row[0].As<Type>()!.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .OrderBy(value => value)
            .ToArray();

        Assert.Equal(
            ["CountriesController.GetAllCountriesAsync", "CurrenciesController.GetAllCurrenciesAsync"],
            anonymous);
    }

    [Fact]
    public void EndpointSet_MatchesAllLegacyCountryCurrencyAndMaterialRoutes()
    {
        string[] expected =
        [
            "DELETE /Countries/{id}", "GET /Countries", "GET /Countries/{id}", "POST /Countries", "PUT /Countries/{id}",
            "DELETE /Currencies/{id:int}", "GET /Currencies", "GET /Currencies/{id:int}", "POST /Currencies", "PUT /Currencies/{id:int}",
            "GET /currencies/exchangerates",
            "DELETE /Materials/{id:int}", "GET /Materials", "GET /Materials/{id:int}", "GET /Materials/{id:int}/suppliers",
            "GET /Materials/machinable", "GET /Materials/printable", "POST /Materials", "POST /Materials/{materialId:int}/suppliers/{supplierId:int}", "PUT /Materials/{id:int}",
            "DELETE /materials/Colors/{id:int}", "DELETE /materials/{materialId:int}/colors/{colorId:int}", "GET /materials/Colors",
            "GET /materials/Colors/{id:int}", "GET /materials/{id:int}/colors", "GET /materials/{materialId:int}/colors/{colorId:int}",
            "POST /materials/Colors", "POST /materials/{materialId:int}/colors/{colorId:int}", "PUT /materials/Colors/{id:int}",
            "DELETE /materials/MaterialGroups/{id:int}", "GET /materials/MaterialGroups", "GET /materials/MaterialGroups/{id:int}",
            "POST /materials/MaterialGroups", "PUT /materials/MaterialGroups/{id:int}",
            "DELETE /materials/SurfaceFinishes/{id:int}", "DELETE /materials/{materialId:int}/surfacefinishes/{surfaceFinishId:int}",
            "GET /materials/SurfaceFinishes", "GET /materials/SurfaceFinishes/{id:int}", "GET /materials/{id:int}/surfacefinishes",
            "GET /materials/{materialId:int}/surfacefinishes/{surfaceFinishId:int}", "POST /materials/SurfaceFinishes",
            "POST /materials/{materialId:int}/surfacefinishes/{surfaceFinishId:int}", "PUT /materials/SurfaceFinishes/{id:int}",
        ];

        var actual = ControllerRoutes.Select(row => row[0].As<Type>()!).SelectMany(GetEndpoints).OrderBy(value => value).ToArray();
        Assert.Equal(expected.OrderBy(value => value), actual);
    }

    [Fact]
    public void ProtectedActions_UseGranularPermissions()
    {
        var protectedActions = ControllerRoutes.SelectMany(row => row[0].As<Type>()!.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
            .Where(method => method.GetCustomAttribute<AllowAnonymousAttribute>() is null);

        foreach (var method in protectedActions)
        {
            var permission = method.GetCustomAttribute<RequirePermissionAttribute>();
            Assert.NotNull(permission);
        }
    }

    [Fact]
    public void Responses_UseLegacyPascalCaseAndOmitNulls()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        var json = JsonSerializer.Serialize(new CountryResponse(7, "Thailand", null, "+66", "TH", "THA", null, null), options);
        using var document = JsonDocument.Parse(json);

        Assert.Contains("\"Id\":7", json, StringComparison.Ordinal);
        Assert.Equal("+66", document.RootElement.GetProperty("CountryCode").GetString());
        Assert.DoesNotContain("\"id\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Continent", json, StringComparison.Ordinal);
    }

    private static IEnumerable<string> GetEndpoints(Type controllerType)
    {
        var controllerName = controllerType.Name.Replace("Controller", string.Empty, StringComparison.Ordinal);
        var prefix = controllerType.GetCustomAttribute<RouteAttribute>()!.Template.Replace("[controller]", controllerName, StringComparison.Ordinal);
        foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            foreach (var httpAttribute in method.GetCustomAttributes().OfType<HttpMethodAttribute>())
            {
                var template = httpAttribute.Template;
                var path = template?.StartsWith('/') == true
                    ? template
                    : string.IsNullOrEmpty(template) ? $"/{prefix}" : $"/{prefix}/{template}";
                foreach (var httpMethod in httpAttribute.HttpMethods)
                {
                    yield return $"{httpMethod} {path}";
                }
            }
        }
    }
}

internal static class TestDataExtensions
{
    public static T? As<T>(this object? value) => value is T typed ? typed : default;
}
