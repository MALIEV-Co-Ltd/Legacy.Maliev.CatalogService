using Legacy.Maliev.CatalogService.Application.Models;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Legacy.Maliev.CatalogService.Api.OpenApi;

/// <summary>Describes the selected optional physical Material decimals without changing other contracts.</summary>
public sealed class MaterialPhysicalDecimalSchemaOptions : IConfigureNamedOptions<OpenApiOptions>
{
    private static readonly string[] PhysicalDecimals =
        [nameof(UpsertMaterialRequest.HardnessBrinell), nameof(UpsertMaterialRequest.HardnessKnoop), nameof(UpsertMaterialRequest.HardnessRockwellA), nameof(UpsertMaterialRequest.HardnessRockwellB), nameof(UpsertMaterialRequest.HardnessRockwellC), nameof(UpsertMaterialRequest.HardnessVickers), nameof(UpsertMaterialRequest.DensityKilogramPerCubicMeter), nameof(UpsertMaterialRequest.TensileStrengthUltimateGigaPascal), nameof(UpsertMaterialRequest.TensileStrengthYieldMegaPascal), nameof(UpsertMaterialRequest.MachinabilityPercent), nameof(UpsertMaterialRequest.ShearModulusGigaPascal), nameof(UpsertMaterialRequest.ThermalConductivityWattPerMeterKelvin)];

    /// <inheritdoc />
    public void Configure(OpenApiOptions options) => Configure(Options.DefaultName, options);

    /// <inheritdoc />
    public void Configure(string? name, OpenApiOptions options)
    {
        if (name != "v1") return;
        options.AddSchemaTransformer((schema, context, _) =>
        {
            if (context.JsonTypeInfo.Type == typeof(decimal?) && context.JsonPropertyInfo?.CustomConverter is LegacyPhysicalDecimalJsonConverter)
            {
                schema.Type = JsonSchemaType.Number | JsonSchemaType.String | JsonSchemaType.Null;
                schema.Format = "double";
                schema.Pattern = null;
            }
            if (context.JsonTypeInfo.Type == typeof(UpsertMaterialRequest) || context.JsonTypeInfo.Type == typeof(MaterialResponse))
                foreach (var propertyName in PhysicalDecimals)
                    schema.Required?.Remove(propertyName);
            return Task.CompletedTask;
        });
    }
}
