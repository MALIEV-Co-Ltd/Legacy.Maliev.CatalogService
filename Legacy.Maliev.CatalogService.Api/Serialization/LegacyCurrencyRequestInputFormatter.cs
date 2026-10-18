using System.Globalization;
using System.Text;
using Legacy.Maliev.CatalogService.Application.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Legacy.Maliev.CatalogService.Api.Serialization;

/// <summary>Preserves the original five-field Currency write admission before projecting writable names.</summary>
internal sealed class LegacyCurrencyRequestInputFormatter : TextInputFormatter
{
    private static readonly JsonSerializerSettings SourceSettings = new()
    {
        ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
        Culture = CultureInfo.InvariantCulture,
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        MetadataPropertyHandling = MetadataPropertyHandling.Default,
        TypeNameHandling = TypeNameHandling.None,
        MaxDepth = 32,
        CheckAdditionalContent = false,
    };

    internal LegacyCurrencyRequestInputFormatter()
    {
        SupportedEncodings.Add(UTF8EncodingWithoutBOM);
        SupportedEncodings.Add(UTF16EncodingLittleEndian);
        SupportedMediaTypes.Add("application/json");
        SupportedMediaTypes.Add("text/json");
        SupportedMediaTypes.Add("application/*+json");
    }

    /// <inheritdoc />
    public override bool CanRead(InputFormatterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var method = context.HttpContext.Request.Method;
        return (HttpMethods.IsPost(method) || HttpMethods.IsPut(method)) && base.CanRead(context);
    }

    /// <inheritdoc />
    protected override bool CanReadType(Type type) => type == typeof(UpsertCurrencyRequest);

    /// <inheritdoc />
    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
    {
        var request = context.HttpContext.Request;
        // Match the stock Newtonsoft formatter's 30 KiB spill threshold; never read the network synchronously.
        await using var buffer = new FileBufferingReadStream(request.Body, 30 * 1024);
        await buffer.DrainAsync(context.HttpContext.RequestAborted);
        buffer.Position = 0;
        using var text = context.ReaderFactory(buffer, encoding);
        using var reader = new JsonTextReader(text) { CloseInput = false };
        SourceCurrency? source;
        try
        {
            // Explicit settings avoid process-wide JsonConvert.DefaultSettings and per-request reference state.
            source = JsonSerializer.Create(SourceSettings).Deserialize<SourceCurrency>(reader);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or OverflowException or DecoderFallbackException)
        {
            context.ModelState.TryAddModelError(context.ModelName, "The currency request body is invalid.");
            return InputFormatterResult.Failure();
        }

        return source is null
            ? InputFormatterResult.NoValue()
            : InputFormatterResult.Success(new UpsertCurrencyRequest(source.ShortName!, source.LongName!));
    }

    // These server-owned fields must be parsed, but only the two names reach the existing validated request DTO.
    private sealed class SourceCurrency
    {
        public int Id { get; set; }
        public string? ShortName { get; set; }
        public string? LongName { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}
