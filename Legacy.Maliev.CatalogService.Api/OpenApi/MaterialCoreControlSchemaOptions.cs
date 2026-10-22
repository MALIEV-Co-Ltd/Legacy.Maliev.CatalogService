using Legacy.Maliev.CatalogService.Application.Models;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Legacy.Maliev.CatalogService.Api.OpenApi;

/// <summary>Describes optional Material request controls while preserving native response metadata.</summary>
public sealed class MaterialCoreControlSchemaOptions : IConfigureNamedOptions<OpenApiOptions>
{
    private static readonly string[] Controls =
        [nameof(UpsertMaterialRequest.MaterialGroupId), nameof(UpsertMaterialRequest.Machinable), nameof(UpsertMaterialRequest.Printable)];

    /// <inheritdoc />
    public void Configure(OpenApiOptions options) => Configure(Options.DefaultName, options);

    /// <inheritdoc />
    public void Configure(string? name, OpenApiOptions options)
    {
        if (name != "v1") return;
        options.AddSchemaTransformer((schema, context, _) =>
        {
            if (context.JsonTypeInfo.Type == typeof(int) && context.JsonPropertyInfo?.Name == nameof(UpsertMaterialRequest.MaterialGroupId) && context.JsonPropertyInfo.CustomConverter is LegacyMaterialGroupIdJsonConverter)
            {
                schema.Type = JsonSchemaType.Integer | JsonSchemaType.String | JsonSchemaType.Null;
                schema.Format = "int32";
                schema.Pattern = null;
                schema.Default = System.Text.Json.Nodes.JsonValue.Create(0);
            }
            if (context.JsonTypeInfo.Type == typeof(bool) && (context.JsonPropertyInfo?.Name == nameof(UpsertMaterialRequest.Machinable) || context.JsonPropertyInfo?.Name == nameof(UpsertMaterialRequest.Printable)) && context.JsonPropertyInfo.CustomConverter is LegacyQuotedBooleanJsonConverter)
            {
                schema.Type = JsonSchemaType.Boolean | JsonSchemaType.String | JsonSchemaType.Number | JsonSchemaType.Null;
                schema.Format = null;
                schema.Pattern = null;
                schema.Default = System.Text.Json.Nodes.JsonValue.Create(false);
            }
            if (context.JsonTypeInfo.Type == typeof(UpsertMaterialRequest))
                foreach (var propertyName in Controls)
                    schema.Required?.Remove(propertyName);
            return Task.CompletedTask;
        });
    }
}
