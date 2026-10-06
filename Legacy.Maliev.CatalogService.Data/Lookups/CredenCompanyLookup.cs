using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Lookups;
using Microsoft.Extensions.Caching.Distributed;

namespace Legacy.Maliev.CatalogService.Data.Lookups;
/// <summary>Live access remains disabled until a provider usage review is recorded.</summary>
public sealed class CredenOptions
{
    /// <summary>Explicit opt-in; false by default.</summary>
    public bool Enabled { get; set; }
    /// <summary>Reference to reviewed access terms, contract and production limits.</summary>
    public string? AccessReviewReference { get; set; }
    /// <summary>Provider origin, restricted to Creden HTTPS.</summary>
    public string BaseUrl { get; set; } = "https://data.creden.co/";
    /// <summary>Total request budget, one attempt.</summary>
    public int TimeoutSeconds { get; set; } = 3;
    /// <summary>Successful/no-match cache TTL.</summary>
    public int CacheSeconds { get; set; } = 300;
    /// <summary>Per-instance upstream calls per minute. Deployment must bound replica totals.</summary>
    public int RequestsPerMinute { get; set; } = 30;
}

/// <summary>Bounded experimental suggestion adapter; never invents company detail facts.</summary>
public sealed class CredenCompanyLookup(HttpClient http, IDistributedCache cache, CredenOptions options, TimeProvider clock) : ICompanyLookup, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset windowStart;
    private int calls;
    /// <inheritdoc/>
    public async Task<CompanyLookup> SearchAsync(string query, string queryType, string language, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        query = ThaiAddressLookup.NormalizeDigits(query.Trim());
        if (query.Length is < 2 or > 128 || query.Any(char.IsControl) || limit is < 1 or > 50 || language is not ("th" or "en") || queryType is not ("name" or "tax-id") || queryType == "tax-id" && (query.Length != 13 || query.Any(c => c is < '0' or > '9')))
            throw new ArgumentException("Invalid company lookup input.");
        // Tax-ID support has not been confirmed for the suggestion contract.
        if (queryType == "tax-id")
            return new("unsupported", []);
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.AccessReviewReference) || !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var origin) || origin.Scheme != "https" || origin.Host != "data.creden.co" || origin.Port != 443 || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || options.TimeoutSeconds is < 1 or > 10 || options.CacheSeconds is < 1 or > 3600 || options.RequestsPerMinute is < 1 or > 60)
            return new("unavailable", []);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var token = budget.Token;
        var key = "legacy:catalog:creden:v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{origin}|{options.AccessReviewReference}|{language}|{limit}|{query}")));
        var acquired = false;
        try
        {
            var cached = await cache.GetAsync(key, token);
            if (cached is { Length: <= 65536 })
            {
                try
                {
                    var value = JsonSerializer.Deserialize<CompanyLookup>(cached);
                    if (value is not null && value.Items is not null && value.Items.Count <= limit
                        && (value.Outcome == "no-match" && value.Items.Count == 0 || value.Outcome == "matches" && value.Items.Count > 0)
                        && value.Items.All(item => item is not null && (!string.IsNullOrWhiteSpace(item.NameTh) || !string.IsNullOrWhiteSpace(item.NameEn))
                            && item.NameTh?.Length is not > 512 && item.NameEn?.Length is not > 512
                            && (item.TaxId is null || item.TaxId.Length == 13 && item.TaxId.All(c => c is >= '0' and <= '9'))))
                        return value;
                }
                catch (JsonException)
                { /* Corrupt cache is not a successful lookup. */
                }
            }

            acquired = await gate.WaitAsync(0, token);
            if (!acquired)
                return new("unavailable", []);
            var now = clock.GetUtcNow();
            if (now - windowStart >= TimeSpan.FromMinutes(1))
            {
                windowStart = now;
                calls = 0;
            }

            if (calls >= options.RequestsPerMinute)
                return new("unavailable", []);
            calls++;
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, "sapi/search/get_suggestion"))
            {
                Content = JsonContent.Create(new { type_search = "prefix", text = query, lang = language })
            };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 65536)
                return new("unavailable", []);
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var bytes = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(), token)) != 0)
            {
                if (bytes.Length + read > 65536)
                    return new("unavailable", []);
                bytes.Write(buffer, 0, read);
            }

            using var document = JsonDocument.Parse(bytes.ToArray());
            var root = document.RootElement;
            if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array)
                return new("unavailable", []);
            var items = new List<CompanySuggestion>();
            foreach (var result in results.EnumerateArray())
            {
                if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("company_name", out var names) || names.ValueKind != JsonValueKind.Object)
                    return new("unavailable", []);
                string? Name(string name) => names.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()) ? n.GetString() : null;
                var th = Name("th");
                var en = Name("en");
                if (th is null && en is null || th?.Length > 512 || en?.Length > 512)
                    return new("unavailable", []);
                var id = result.TryGetProperty("id", out var identifier) && identifier.ValueKind == JsonValueKind.String ? identifier.GetString() : null;
                if (id is not null && (id.Length != 13 || id.Any(c => c is < '0' or > '9')))
                    return new("unavailable", []);
                items.Add(new(th, en, id, now));
            }

            var lookup = new CompanyLookup(items.Count == 0 ? "no-match" : "matches", items.Take(limit).ToArray())
            {
                HasMore = items.Count > limit
            };
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(lookup), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.CacheSeconds) }, token);
            return lookup;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new("unavailable", []);
        }
        catch (HttpRequestException)
        {
            return new("unavailable", []);
        }
        catch (IOException)
        {
            return new("unavailable", []);
        }
        catch (JsonException)
        {
            return new("unavailable", []);
        }
        catch (InvalidOperationException)
        {
            return new("unavailable", []);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new("unavailable", []);
        }
        finally
        {
            if (acquired)
                gate.Release();
        }
    }

    /// <summary>Releases instance-owned resources.</summary>
    public void Dispose()
    {
        gate.Dispose();
        http.Dispose();
    }
}
