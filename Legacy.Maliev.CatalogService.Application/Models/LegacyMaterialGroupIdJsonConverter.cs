using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.CatalogService.Application.Models;

/// <summary>Preserves the original typed Material group identifier reader and zero default.</summary>
public sealed class LegacyMaterialGroupIdJsonConverter : JsonConverter<int>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.Number and not JsonTokenType.String and not JsonTokenType.Null)
            throw new JsonException("The value is not a valid legacy Material group identifier.");
        using var token = JsonDocument.ParseValue(ref reader);
        using var text = new StringReader(token.RootElement.GetRawText());
        using var legacy = new Newtonsoft.Json.JsonTextReader(text)
        {
            Culture = CultureInfo.InvariantCulture,
            FloatParseHandling = Newtonsoft.Json.FloatParseHandling.Double,
        };
        try
        {
            return legacy.ReadAsInt32() ?? 0;
        }
        catch (Newtonsoft.Json.JsonException exception)
        {
            throw new JsonException("The value is not a valid legacy Material group identifier.", exception);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
