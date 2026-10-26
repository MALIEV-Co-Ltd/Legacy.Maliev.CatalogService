using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.CatalogService.Application.Models;

/// <summary>Validates ignored typed identity and audit metadata from the original Country request model.</summary>
public sealed class LegacyCountryRequestJsonConverter : JsonConverter<UpsertCountryRequest>
{
    /// <inheritdoc />
    public override UpsertCountryRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a Country request object.");
        var comparison = options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var isId = string.Equals(property.Name, nameof(CountryResponse.Id), comparison);
                if (isId || string.Equals(property.Name, nameof(CountryResponse.CreatedDate), comparison) ||
                    string.Equals(property.Name, nameof(CountryResponse.ModifiedDate), comparison))
                {
                    ValidateIgnored(property.Value, isId);
                    continue;
                }
                writer.WritePropertyName(property.Name);
                writer.WriteRawValue(property.Value.GetRawText());
            }
            writer.WriteEndObject();
        }
        return JsonSerializer.Deserialize<UpsertCountryRequest>(buffer.ToArray(), InnerOptions(options))
            ?? throw new JsonException("Expected a Country request object.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, UpsertCountryRequest value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, InnerOptions(options));

    private JsonSerializerOptions InnerOptions(JsonSerializerOptions options)
    {
        var inner = new JsonSerializerOptions(options);
        for (var index = inner.Converters.Count - 1; index >= 0; index--)
            if (ReferenceEquals(inner.Converters[index], this)) inner.Converters.RemoveAt(index);
        return inner;
    }

    private static void ValidateIgnored(JsonElement value, bool isId)
    {
        using var text = new StringReader(value.GetRawText());
        using var legacy = new Newtonsoft.Json.JsonTextReader(text) { Culture = CultureInfo.InvariantCulture };
        try
        {
            if (isId) _ = legacy.ReadAsInt32();
            else _ = legacy.ReadAsDateTime();
        }
        catch (Newtonsoft.Json.JsonException exception)
        {
            throw new JsonException("The ignored Country metadata has an invalid legacy type.", exception);
        }
    }
}
