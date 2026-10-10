using System.Text.Json;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.CatalogService.Application.Models;

/// <summary>Preserves ignored group identifier occurrences within a single Material request body.</summary>
public sealed class LegacyMaterialRequestJsonConverter : JsonConverter<UpsertMaterialRequest>
{
    /// <inheritdoc />
    public override UpsertMaterialRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a Material request object.");
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var comparison = options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (string.Equals(property.Name, nameof(UpsertMaterialRequest.MaterialGroupId), comparison) &&
                    (property.Value.ValueKind == JsonValueKind.Null ||
                    (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == string.Empty)))
                    continue;
                writer.WritePropertyName(property.Name);
                writer.WriteRawValue(property.Value.GetRawText());
            }
            writer.WriteEndObject();
        }
        return JsonSerializer.Deserialize<UpsertMaterialRequest>(buffer.ToArray(), InnerOptions(options))
            ?? throw new JsonException("Expected a Material request object.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, UpsertMaterialRequest value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, InnerOptions(options));

    private JsonSerializerOptions InnerOptions(JsonSerializerOptions options)
    {
        var inner = new JsonSerializerOptions(options);
        for (var index = inner.Converters.Count - 1; index >= 0; index--)
            if (ReferenceEquals(inner.Converters[index], this)) inner.Converters.RemoveAt(index);
        return inner;
    }
}
