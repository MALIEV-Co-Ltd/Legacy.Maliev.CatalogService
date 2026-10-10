using Legacy.Maliev.CatalogService.Application.Models;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.CatalogService.Api.OpenApi;

/// <summary>Aligns the selected optional Material string schema metadata with omitted wire values.</summary>
public sealed class MaterialStringSchemaOptions : IConfigureNamedOptions<OpenApiOptions>
{
    private static readonly string[] OptionalStrings =
        [nameof(UpsertMaterialRequest.Aisi), nameof(UpsertMaterialRequest.Din), nameof(UpsertMaterialRequest.Bts), nameof(UpsertMaterialRequest.Jis), nameof(UpsertMaterialRequest.Uns), nameof(UpsertMaterialRequest.En), nameof(UpsertMaterialRequest.Afnor), nameof(UpsertMaterialRequest.Uni), nameof(UpsertMaterialRequest.Sis), nameof(UpsertMaterialRequest.Sae), nameof(UpsertMaterialRequest.Astm), nameof(UpsertMaterialRequest.Ams), nameof(UpsertMaterialRequest.MaterialNumber), nameof(UpsertMaterialRequest.ManufacturerReference), nameof(UpsertMaterialRequest.Url), nameof(UpsertMaterialRequest.Comment)];

    /// <inheritdoc />
    public void Configure(OpenApiOptions options) => Configure(Options.DefaultName, options);

    /// <inheritdoc />
    public void Configure(string? name, OpenApiOptions options)
    {
        if (name != "v1") return;
        options.AddSchemaTransformer((schema, context, _) =>
        {
            if (context.JsonTypeInfo.Type == typeof(UpsertMaterialRequest) || context.JsonTypeInfo.Type == typeof(MaterialResponse))
                foreach (var propertyName in OptionalStrings)
                    schema.Required?.Remove(propertyName);
            return Task.CompletedTask;
        });
    }
}
