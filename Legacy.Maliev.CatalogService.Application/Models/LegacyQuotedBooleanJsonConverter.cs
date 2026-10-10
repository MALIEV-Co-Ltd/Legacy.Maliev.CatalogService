using System.Text.Json;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.CatalogService.Application.Models;

/// <summary>Accepts legacy quoted Boolean material flags while retaining Boolean JSON output.</summary>
public sealed class LegacyQuotedBooleanJsonConverter : JsonConverter<bool>
{
    /// <inheritdoc />
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.True or JsonTokenType.False) return reader.GetBoolean();
        if (reader.TokenType == JsonTokenType.String && bool.TryParse(reader.GetString(), out var value)) return value;
        throw new JsonException("Expected a Boolean or a quoted Boolean value.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
        writer.WriteBooleanValue(value);
}
