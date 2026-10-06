using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Lookups;
using Legacy.Maliev.CatalogService.Data.Lookups;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Time.Testing;

namespace Legacy.Maliev.CatalogService.Tests.Lookups;

public sealed class CredenCompanyLookupTests
{
    private const string Valid = "{\"success\":true,\"data\":{\"result\":[{\"id\":\"0105559999999\",\"company_name\":{\"th\":\"บริษัท ทดสอบ จำกัด\",\"en\":\"Test Limited\"}}]}}";
    private static CredenOptions Enabled() => new()
    {
        Enabled = true,
        AccessReviewReference = "fixture-contract-only"
    };
    [Fact]
    public async Task Disabled_or_unreviewed_configuration_never_calls_provider()
    {
        using var fixture = new Fixture(new());
        Assert.Equal("unavailable", (await fixture.Search()).Outcome);
        Assert.Equal(0, fixture.Handler.Calls);
        fixture.Options.Enabled = true;
        Assert.Equal("unavailable", (await fixture.Search()).Outcome);
        Assert.Equal(0, fixture.Handler.Calls);
    }

    [Theory]
    [InlineData("http://data.creden.co/")]
    [InlineData("https://other.example/")]
    [InlineData("https://data.creden.co/path")]
    [InlineData("https://data.creden.co/?q=x")]
    public async Task Rejects_unapproved_provider_origins(string origin)
    {
        using var fixture = new Fixture(Enabled());
        fixture.Options.BaseUrl = origin;
        Assert.Equal("unavailable", (await fixture.Search()).Outcome);
        Assert.Equal(0, fixture.Handler.Calls);
    }

    [Theory]
    [InlineData("ทดสอบ", "th")]
    [InlineData("Test", "en")]
    public async Task Actual_payload_and_truthful_names_are_mapped_without_fabricated_details(string query, string language)
    {
        using var fixture = new Fixture(Enabled());
        fixture.Handler.Response = async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://data.creden.co/sapi/search/get_suggestion", request.RequestUri?.AbsoluteUri);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(query, body.RootElement.GetProperty("text").GetString());
            Assert.Equal(language, body.RootElement.GetProperty("lang").GetString());
            Assert.Equal("prefix", body.RootElement.GetProperty("type_search").GetString());
            return Json(Valid);
        };
        var result = await fixture.Lookup.SearchAsync(query, "name", language, 20, default);
        Assert.Equal("matches", result.Outcome);
        var item = Assert.Single(result.Items);
        Assert.Equal("บริษัท ทดสอบ จำกัด", item.NameTh);
        Assert.Equal("Test Limited", item.NameEn);
        Assert.Equal("0105559999999", item.TaxId);
        Assert.Null(item.Status);
        Assert.Null(item.CompanyType);
        Assert.Null(item.Objectives);
        Assert.Null(item.RegisteredAddress);
        Assert.Equal("suggestion", result.Capability);
        Assert.Equal("creden", result.Provider);
        Assert.Equal(fixture.Clock.GetUtcNow(), item.RetrievedAt);
    }

    [Fact]
    public async Task Validated_tax_id_is_explicitly_unsupported_not_fake_no_match()
    {
        using var fixture = new Fixture(Enabled());
        Assert.Equal("unsupported", (await fixture.Lookup.SearchAsync("๐๑๐๕๕๕๙๙๙๙๙๙๙", "tax-id", "th", 20, default)).Outcome);
        Assert.Equal(0, fixture.Handler.Calls);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Lookup.SearchAsync("123", "tax-id", "th", 20, default));
    }

    [Theory]
    [InlineData("", "name", "th", 20)]
    [InlineData("a", "name", "th", 20)]
    [InlineData("valid", "other", "th", 20)]
    [InlineData("valid", "name", "xx", 20)]
    [InlineData("valid", "name", "th", 0)]
    [InlineData("valid", "name", "th", 51)]
    [InlineData("a\nb", "name", "th", 20)]
    public async Task Invalid_requests_do_not_contact_provider(string query, string kind, string lang, int limit)
    {
        using var fixture = new Fixture(Enabled());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Lookup.SearchAsync(query, kind, lang, limit, default));
        Assert.Equal(0, fixture.Handler.Calls);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{}")]
    [InlineData("{\"success\":false}")]
    [InlineData("{\"success\":true,\"data\":{\"result\":[{}]}}")]
    [InlineData("{\"success\":true,\"data\":{\"result\":[{\"company_name\":{}}]}}")]
    [InlineData("{\"success\":true,\"data\":{\"result\":[{\"id\":\"x\",\"company_name\":{\"en\":\"Test\"}}]}}")]
    public async Task Malformed_responses_are_unavailable_and_not_cached(string body)
    {
        using var fixture = new Fixture(Enabled());
        fixture.Handler.Response = (_, _) => Task.FromResult(Json(body));
        Assert.Equal("unavailable", (await fixture.Search()).Outcome);
        Assert.Equal("unavailable", (await fixture.Search()).Outcome);
        Assert.Equal(2, fixture.Handler.Calls);
        Assert.Empty(fixture.Cache.Values);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(302)]
    public async Task Provider_failure_is_explicit_without_retry(int status)
    {
        using var fixture = new Fixture(Enabled());
        fixture.Handler.Response = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        Assert.Equal("unavailable", (await fixture.Search()).Outcome);
        Assert.Equal(1, fixture.Handler.Calls);
        Assert.Empty(fixture.Cache.Values);
    }

    [Fact]
    public async Task Cache_is_namespaced_hashed_and_expires_without_caching_failure()
    {
        using var fixture = new Fixture(Enabled());
        Assert.Equal("matches", (await fixture.Search()).Outcome);
        Assert.Equal("matches", (await fixture.Search()).Outcome);
        Assert.Equal(1, fixture.Handler.Calls);
        var key = Assert.Single(fixture.Cache.Values).Key;
        Assert.StartsWith("legacy:catalog:creden:v1:", key);
        Assert.DoesNotContain("Test", key);
        fixture.Clock.Advance(TimeSpan.FromSeconds(301));
        Assert.Equal("matches", (await fixture.Search()).Outcome);
        Assert.Equal(2, fixture.Handler.Calls);
    }

    [Fact]
    public async Task Empty_array_is_no_match_and_cached_but_null_details_stay_null()
    {
        using var fixture = new Fixture(Enabled());
        fixture.Handler.Response = (_, _) => Task.FromResult(Json("{\"success\":true,\"data\":{\"result\":[]}}"));
        Assert.Equal("no-match", (await fixture.Search()).Outcome);
        Assert.Equal("no-match", (await fixture.Search()).Outcome);
        Assert.Equal(1, fixture.Handler.Calls);
    }

    [Theory]
    [InlineData("{\"outcome\":\"matches\",\"items\":null}")]
    [InlineData("{\"outcome\":\"matches\",\"items\":[]}")]
    [InlineData("{\"outcome\":\"matches\",\"items\":[null]}")]
    [InlineData("{\"outcome\":\"matches\",\"items\":[{\"nameEn\":\"Test\",\"taxId\":\"invalid\"}]}")]
    public async Task Structurally_corrupt_cache_never_becomes_success_without_provider_validation(string cached)
    {
        using var fixture = new Fixture(Enabled());
        Assert.Equal("matches", (await fixture.Search()).Outcome);
        var key = Assert.Single(fixture.Cache.Values).Key;
        fixture.Cache.Values[key] = (Encoding.UTF8.GetBytes(cached), fixture.Clock.GetUtcNow().AddMinutes(1));
        Assert.Equal("matches", (await fixture.Search()).Outcome);
        Assert.Equal(2, fixture.Handler.Calls);
    }

    [Fact]
    public async Task Concurrent_cache_misses_do_not_create_unbounded_waiters_or_duplicate_requests()
    {
        using var fixture = new Fixture(Enabled());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return Json(Valid);
        };
        var first = fixture.Search();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var second = await fixture.Lookup.SearchAsync("Other", "name", "th", 20, default);
            Assert.Equal("unavailable", second.Outcome);
            Assert.Equal(1, fixture.Handler.Calls);
        }
        finally
        {
            release.TrySetResult();
            await first;
        }
    }

    [Fact]
    public async Task Rate_budget_caps_distinct_upstream_requests_and_resets()
    {
        using var fixture = new Fixture(Enabled());
        fixture.Options.RequestsPerMinute = 1;
        Assert.Equal("matches", (await fixture.Search()).Outcome);
        Assert.Equal("unavailable", (await fixture.Lookup.SearchAsync("other", "name", "th", 20, default)).Outcome);
        Assert.Equal(1, fixture.Handler.Calls);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("matches", (await fixture.Lookup.SearchAsync("other", "name", "th", 20, default)).Outcome);
        Assert.Equal(2, fixture.Handler.Calls);
    }

    [Fact]
    public async Task Timeout_is_unavailable_but_caller_cancellation_propagates()
    {
        using var fixture = new Fixture(Enabled());
        fixture.Options.TimeoutSeconds = 1;
        fixture.Handler.Response = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Json(Valid);
        };
        Assert.Equal("unavailable", (await fixture.Search()).Outcome);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Lookup.SearchAsync("Test", "name", "th", 20, cancellation.Token));
    }

    [Fact]
    public async Task Oversized_response_and_transport_error_are_not_empty_successes()
    {
        using var fixture = new Fixture(Enabled());
        fixture.Handler.Response = (_, _) => Task.FromResult(Json(new string('x', 65537)));
        Assert.Equal("unavailable", (await fixture.Search()).Outcome);
        fixture.Handler.Response = (_, _) => throw new HttpRequestException("fixture transport failure");
        Assert.Equal("unavailable", (await fixture.Search()).Outcome);
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };
    private sealed class Fixture : IDisposable
    {
        public CredenOptions Options { get; }
        public FakeTimeProvider Clock { get; } = new();
        public Handler Handler { get; } = new();
        public TestCache Cache { get; }
        public CredenCompanyLookup Lookup { get; }

        public Fixture(CredenOptions options)
        {
            Options = options;
            Cache = new(Clock);
            Lookup = new(new HttpClient(Handler), Cache, Options, Clock);
        }

        public Task<CompanyLookup> Search() => Lookup.SearchAsync("Test", "name", "th", 20, default);
        public void Dispose() => Lookup.Dispose();
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Response { get; set; } = (_, _) => Task.FromResult(Json(Valid));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Response(request, cancellationToken);
        }
    }

    private sealed class TestCache(TimeProvider clock) : IDistributedCache
    {
        public Dictionary<string, (byte[] Data, DateTimeOffset Expiry)> Values { get; } = [];

        public byte[]? Get(string key) => Values.TryGetValue(key, out var value) && value.Expiry > clock.GetUtcNow() ? value.Data : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Values[key] = (value, clock.GetUtcNow() + options.AbsoluteExpirationRelativeToNow!.Value);
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => Values.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
