using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.CatalogService.Application.Models;

/// <summary>Accepts scoped legacy material flag inputs while retaining Boolean JSON output.</summary>
public sealed class LegacyQuotedBooleanJsonConverter : JsonConverter<bool>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return false;
        if (reader.TokenType == JsonTokenType.Number) return ReadNumber(ref reader);
        if (reader.TokenType is JsonTokenType.True or JsonTokenType.False) return reader.GetBoolean();
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected a supported material Boolean value.");
        var text = reader.GetString();
        if (string.IsNullOrEmpty(text)) return false;
        if (bool.TryParse(text, out var value)) return value;
        throw new JsonException("Expected a Boolean or a quoted Boolean value.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
        writer.WriteBooleanValue(value);

    private static bool ReadNumber(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var literal = document.RootElement.GetRawText();
        if (literal.IndexOfAny(['.', 'e', 'E']) < 0)
        {
            // Original numeric Boolean reading uses BigInteger after Int64 overflow,
            // with a 380-character limit including a leading minus sign.
            if (literal.Length > 380 || !BigInteger.TryParse(literal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                throw new JsonException("Material Boolean integer exceeds the supported legacy boundary.");
            return !integer.IsZero;
        }

        // Original default Double parsing yields signed infinity on overflow and
        // signed zero on underflow; its Boolean conversion compares with zero.
        if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            throw new JsonException("Expected a valid numeric material Boolean value.");
        return number != 0;
    }
}
