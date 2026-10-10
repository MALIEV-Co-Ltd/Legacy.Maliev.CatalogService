using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.CatalogService.Application.Models;

/// <summary>Preserves standard JSON scalar conversion for selected legacy string properties.</summary>
public sealed class LegacyScalarStringJsonConverter : JsonConverter<string>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Number:
                using (var document = JsonDocument.ParseValue(ref reader))
                {
                    var literal = document.RootElement.GetRawText();
                    if (double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        return literal;
                }
                throw new JsonException("The value is not a valid legacy string scalar.");
            default:
                throw new JsonException("The value is not a valid legacy string scalar.");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
