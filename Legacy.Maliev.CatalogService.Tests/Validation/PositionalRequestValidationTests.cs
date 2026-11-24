using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.CatalogService.Tests.Validation;

public sealed class PositionalRequestValidationTests
{
    [Theory]
    [InlineData(typeof(UpsertCountryRequest), "Name", 50)]
    [InlineData(typeof(UpsertCurrencyRequest), "ShortName", 10)]
    [InlineData(typeof(UpsertCurrencyRequest), "LongName", 50)]
    [InlineData(typeof(UpsertMaterialGroupRequest), "Name", 50)]
    [InlineData(typeof(UpsertColorRequest), "Name", 50)]
    [InlineData(typeof(UpsertSurfaceFinishRequest), "Name", 50)]
    [InlineData(typeof(UpsertMaterialRequest), "Name", 50)]
    public void RequiredNamesUseConstructorValidation(Type requestType, string field, int maxLength)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IObjectModelValidator>();

        foreach (var (value, valid) in new (string?, bool)[]
        {
            (null, false), ("", true), (new string('x', maxLength), true), (new string('x', maxLength + 1), false),
        })
        {
            var fields = requestType == typeof(UpsertCurrencyRequest)
                ? new Dictionary<string, string?> { ["ShortName"] = "ABC", ["LongName"] = "Synthetic" }
                : new Dictionary<string, string?>();
            fields[field] = value;
            var request = JsonSerializer.Deserialize(JsonSerializer.Serialize(fields), requestType);
            Assert.NotNull(request);

            // Only the MVC validator runs. There is no host, controller, or request dispatch.
            var context = new ActionContext(new DefaultHttpContext { RequestServices = scope.ServiceProvider },
                new RouteData(), new ActionDescriptor());
            validator.Validate(context, validationState: null, prefix: "", model: request);

            Assert.Equal(valid, context.ModelState.IsValid);
            if (!valid)
            {
                Assert.Equal(field, Assert.Single(context.ModelState.Keys));
                Assert.Single(context.ModelState[field]!.Errors);
            }
        }
    }
}
