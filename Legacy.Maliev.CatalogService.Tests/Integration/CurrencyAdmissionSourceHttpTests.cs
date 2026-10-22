using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Tests.SourceCompatibility;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

public sealed class CurrencyAdmissionSourceHttpTests(CatalogHttpFixture fixture)
    : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, bool, string> Cases
    {
        get
        {
            var data = new TheoryData<string, bool, string>();
            foreach (var item in CurrencyAdmissionCases.All)
            {
                foreach (var update in new[] { false, true })
                {
                    foreach (var encoding in CurrencyAdmissionCases.Encodings)
                    {
                        data.Add(item.Name, update, encoding);
                    }
                }
            }
            return data;
        }
    }

    public static TheoryData<bool, string, bool> AuthorizationCases
    {
        get
        {
            var data = new TheoryData<bool, string, bool>();
            foreach (var update in new[] { false, true })
            {
                foreach (var mode in new[] { "anonymous", "read", "other-resource", "opposite-write", "expired", "signature", "malformed" })
                {
                    foreach (var malformedBody in new[] { false, true })
                    {
                        data.Add(update, mode, malformedBody);
                    }
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CurrencyWrites_MatchSourceAdmissionAndRetainedGuards_WithDatabaseAndListFreshnessReadback(string name, bool update, string encoding)
    {
        var item = CurrencyAdmissionCases.Find(name);
        using var client = Writer();
        var sentinelId = await CreateAsync(client, "KEEP", "Unchanged currency");
        var sentinel = await fixture.StoredAsync("currencies", sentinelId);
        var id = update ? await CreateAsync(client, "OLD", "Before update") : 0;
        var previous = update ? await fixture.StoredAsync("currencies", id) : default;
        var before = await CurrencySnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        using var anonymous = fixture.CreateAnonymousClient();
        var listBefore = await ReadListAsync(anonymous);
        var start = DateTime.UtcNow.AddSeconds(-5);
        using var content = CurrencyAdmissionCases.Content(item.Body, encoding);
        using var response = update
            ? await client.PutAsync($"/Currencies/{id}", content)
            : await client.PostAsync("/Currencies", content);
        var expected = item.TargetStatus == 200
            ? update ? HttpStatusCode.NoContent : HttpStatusCode.Created
            : (HttpStatusCode)item.TargetStatus;
        Assert.Equal(expected, response.StatusCode);
        if (item.TargetStatus != 200)
        {
            Assert.Equal(before, await CurrencySnapshotAsync());
            Assert.Equal(counts, await fixture.SnapshotAsync());
            Assert.Equal(listBefore, await ReadListAsync(anonymous));
            return;
        }
        if (!update)
        {
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(5, created.RootElement.EnumerateObject().Count());
            id = created.RootElement.GetProperty("Id").GetInt32();
            Assert.NotNull(response.Headers.Location);
            Assert.Equal($"/currencies/{id}", response.Headers.Location.AbsolutePath.ToLowerInvariant());
            using var location = await client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, location.StatusCode);
            counts[1]++;
        }
        Assert.True(id > 0);
        Assert.NotEqual(777777, id);
        Assert.Equal(counts, await fixture.SnapshotAsync());
        Assert.Equal(sentinel.GetRawText(), (await fixture.StoredAsync("currencies", sentinelId)).GetRawText());
        var stored = await fixture.StoredAsync("currencies", id);
        Assert.Equal(item.ShortName, stored.GetProperty("ShortName").GetString());
        Assert.Equal(item.LongName, stored.GetProperty("LongName").GetString());
        Assert.InRange(stored.GetProperty("ModifiedDate").GetDateTime(), start, DateTime.UtcNow.AddSeconds(5));
        if (update)
        {
            Assert.Equal(previous.GetProperty("CreatedDate").GetDateTime(), stored.GetProperty("CreatedDate").GetDateTime());
        }
        else
        {
            Assert.InRange(stored.GetProperty("CreatedDate").GetDateTime(), start, DateTime.UtcNow.AddSeconds(5));
        }
        using var read = await client.GetAsync($"/Currencies/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var detail = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(5, detail.RootElement.EnumerateObject().Count());
        Assert.Equal(item.ShortName, detail.RootElement.GetProperty("ShortName").GetString());
        Assert.Equal(item.LongName, detail.RootElement.GetProperty("LongName").GetString());
        Assert.Equal(stored.GetProperty("CreatedDate").GetDateTime(), detail.RootElement.GetProperty("CreatedDate").GetDateTime());
        Assert.Equal(stored.GetProperty("ModifiedDate").GetDateTime(), detail.RootElement.GetProperty("ModifiedDate").GetDateTime());
        using var list = JsonDocument.Parse(await ReadListAsync(anonymous));
        var listed = list.RootElement.EnumerateArray().Single(row => row.GetProperty("Id").GetInt32() == id);
        Assert.Equal(item.ShortName, listed.GetProperty("ShortName").GetString());
        Assert.Equal(item.LongName, listed.GetProperty("LongName").GetString());
    }

    [Theory]
    [MemberData(nameof(AuthorizationCases))]
    public async Task CurrencyWrites_ProductionJwtAndExactPermission_PrecedeAdmissionWithoutMutation(bool update, string mode, bool malformedBody)
    {
        using var writer = Writer();
        var id = await CreateAsync(writer, "KEEP", "Unchanged currency");
        using var client = mode switch
        {
            "anonymous" => fixture.CreateAnonymousClient(),
            "read" => fixture.CreateClient("legacy-catalog.currencies.read"),
            "other-resource" => fixture.CreateClient("legacy-catalog.countries.create", "legacy-catalog.countries.update"),
            "opposite-write" => fixture.CreateClient(update ? "legacy-catalog.currencies.create" : "legacy-catalog.currencies.update"),
            _ => fixture.CreateAnonymousClient(),
        };
        if (mode is "expired" or "signature" or "malformed")
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                fixture.Token(mode, update ? "legacy-catalog.currencies.update" : "legacy-catalog.currencies.create"));
        }
        var before = await CurrencySnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        using var anonymous = fixture.CreateAnonymousClient();
        var listBefore = await ReadListAsync(anonymous);
        var body = malformedBody
            ? @"{""ShortName"":""THB"",""LongName"":""Thai Baht"",""Id"":[]}"
            : @"{""ShortName"":""THB"",""LongName"":""Thai Baht""}";
        using var content = CurrencyAdmissionCases.Content(body, "utf-8");
        using var response = update
            ? await client.PutAsync($"/Currencies/{id}", content)
            : await client.PostAsync("/Currencies", content);
        Assert.Equal(mode is "anonymous" or "expired" or "signature" or "malformed"
            ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await CurrencySnapshotAsync());
        Assert.Equal(counts, await fixture.SnapshotAsync());
        Assert.Equal(listBefore, await ReadListAsync(anonymous));
    }

    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(999999, HttpStatusCode.NotFound)]
    public async Task CurrencyUpdate_RetainsZeroAndMissingIdentifierGuards(int id, HttpStatusCode status)
    {
        using var client = Writer();
        await CreateAsync(client, "KEEP", "Unchanged currency");
        var before = await CurrencySnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        using var content = CurrencyAdmissionCases.Content(@"{""ShortName"":""THB"",""LongName"":""Thai Baht""}", "utf-8");
        using var response = await client.PutAsync($"/Currencies/{id}", content);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(before, await CurrencySnapshotAsync());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(false, "utf-16BE")]
    [InlineData(true, "utf-16BE")]
    [InlineData(false, "utf-32")]
    [InlineData(true, "utf-32")]
    [InlineData(false, "iso-8859-1")]
    [InlineData(true, "iso-8859-1")]
    public async Task CurrencyWrites_UnsupportedCharsetRejectsWithoutMutation(bool update, string charset)
    {
        using var client = Writer();
        var id = await CreateAsync(client, "KEEP", "Unchanged currency");
        var before = await CurrencySnapshotAsync();
        var counts = await fixture.SnapshotAsync();
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(@"{""ShortName"":""THB"",""LongName"":""Thai Baht""}"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = charset };
        using var response = update
            ? await client.PutAsync($"/Currencies/{id}", content)
            : await client.PostAsync("/Currencies", content);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(before, await CurrencySnapshotAsync());
        Assert.Equal(counts, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task CurrencyWrites_DoNotDisablePostgresOptimisticConcurrency()
    {
        using var client = Writer();
        var id = await CreateAsync(client, "KEEP", "Unchanged currency");
        await using var first = fixture.CreateCurrencyContext();
        await using var stale = fixture.CreateCurrencyContext();
        var firstRow = await first.Currencies.SingleAsync(row => row.Id == id);
        var staleRow = await stale.Currencies.SingleAsync(row => row.Id == id);
        firstRow.LongName = "First writer";
        await first.SaveChangesAsync();
        staleRow.LongName = "Stale writer";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        Assert.Equal("First writer", (await fixture.StoredAsync("currencies", id)).GetProperty("LongName").GetString());
    }

    private HttpClient Writer() => fixture.CreateClient(
        "legacy-catalog.currencies.create", "legacy-catalog.currencies.update", "legacy-catalog.currencies.read");

    private static async Task<int> CreateAsync(HttpClient client, string shortName, string longName)
    {
        using var response = await client.PostAsJsonAsync("/Currencies", new { ShortName = shortName, LongName = longName });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("Id").GetInt32();
    }

    private static async Task<string> ReadListAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/Currencies");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> CurrencySnapshotAsync()
    {
        await using var context = fixture.CreateCurrencyContext();
        var rows = await context.Currencies.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                row.ShortName,
                row.LongName,
                row.CreatedDate,
                row.ModifiedDate,
                Version = EF.Property<uint>(row, "xmin"),
            }).ToArrayAsync();
        return JsonSerializer.Serialize(rows);
    }
}
