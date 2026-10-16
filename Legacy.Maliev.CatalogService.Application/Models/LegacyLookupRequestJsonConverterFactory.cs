using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.CatalogService.Application.Models;

/// <summary>Validates ignored server metadata from the three original Material lookup request models.</summary>
public sealed class LegacyLookupRequestJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(UpsertColorRequest) ||
        typeToConvert == typeof(UpsertMaterialGroupRequest) ||
        typeToConvert == typeof(UpsertSurfaceFinishRequest);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(UpsertColorRequest)) return new LookupConverter<UpsertColorRequest>(this);
        if (typeToConvert == typeof(UpsertMaterialGroupRequest)) return new LookupConverter<UpsertMaterialGroupRequest>(this);
        if (typeToConvert == typeof(UpsertSurfaceFinishRequest)) return new LookupConverter<UpsertSurfaceFinishRequest>(this);
        throw new NotSupportedException("Only the three owned Material lookup request types are supported.");
    }

    private sealed class LookupConverter<T>(LegacyLookupRequestJsonConverterFactory owner) : JsonConverter<T> where T : class
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Expected a lookup request object.");
            var comparison = options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    var isId = string.Equals(property.Name, "Id", comparison);
                    if (isId || string.Equals(property.Name, "CreatedDate", comparison) || string.Equals(property.Name, "ModifiedDate", comparison))
                    {
                        ValidateIgnored(property.Value, isId);
                        continue;
                    }
                    writer.WritePropertyName(property.Name);
                    writer.WriteRawValue(property.Value.GetRawText());
                }
                writer.WriteEndObject();
            }
            return JsonSerializer.Deserialize<T>(buffer.ToArray(), InnerOptions(options))
                ?? throw new JsonException("Expected a lookup request object.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, InnerOptions(options));

        private JsonSerializerOptions InnerOptions(JsonSerializerOptions options)
        {
            var inner = new JsonSerializerOptions(options);
            for (var index = inner.Converters.Count - 1; index >= 0; index--)
                if (ReferenceEquals(inner.Converters[index], owner)) inner.Converters.RemoveAt(index);
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
                throw new JsonException("The ignored server metadata has an invalid legacy type.", exception);
            }
        }
    }
}
