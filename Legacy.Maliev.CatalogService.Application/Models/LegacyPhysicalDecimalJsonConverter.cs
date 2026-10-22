using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.CatalogService.Application.Models;

/// <summary>Preserves the original typed decimal reader for selected physical Material properties.</summary>
public sealed class LegacyPhysicalDecimalJsonConverter : JsonConverter<decimal?>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.Number and not JsonTokenType.String and not JsonTokenType.Null)
            throw new JsonException("The value is not a valid legacy physical decimal.");
        using var token = JsonDocument.ParseValue(ref reader);
        using var text = new StringReader(token.RootElement.GetRawText());
        using var legacy = new Newtonsoft.Json.JsonTextReader(text)
        {
            Culture = CultureInfo.InvariantCulture,
            FloatParseHandling = Newtonsoft.Json.FloatParseHandling.Double,
        };
        try
        {
            return legacy.ReadAsDecimal();
        }
        catch (Newtonsoft.Json.JsonException exception)
        {
            throw new JsonException("The value is not a valid legacy physical decimal.", exception);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is { } number) writer.WriteNumberValue(number);
        else writer.WriteNullValue();
    }
}
