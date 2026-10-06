using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CatalogService.Tests.Integration;

/// <summary>Captures synthetic literal-storage observations; passing diagnostics do not prove source parity.</summary>
public sealed class CountryIsoAndHealthSourceDiagnosticsTests(CatalogHttpFixture fixture)
    : IClassFixture<CatalogHttpFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string?, string?> Codes => new()
    {
        { "empty", "", "" },
        { "short", "T", "T" },
        { "trailing-space", "T ", "TH " },
        { "null", null, null },
        { "bmp-boundary", "TH", "THA" },
        { "supplementary-boundary", "\U0001F600", "\U0001F600A" },
        { "whitespace", "  ", "   " },
    };

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task NormalCountryHttpAndOwnDatabaseCaptureAndAssertExactSourceLiteralParity(
        string caseName, string? iso2, string? iso3)
    {
        using var client = fixture.CreateClient("legacy-catalog.countries.create",
            "legacy-catalog.countries.read", "legacy-catalog.countries.update");
        var request = new { Name = "Synthetic ISO diagnostic", Iso2 = iso2, Iso3 = iso3 };
        using var created = await client.PostAsJsonAsync("/Countries", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        byte[] createdBytes = await created.Content.ReadAsByteArrayAsync();
        using var createdJson = JsonDocument.Parse(createdBytes);
        int id = createdJson.RootElement.GetProperty("Id").GetInt32();

        using var fresh = await client.GetAsync($"/Countries/{id}");
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        byte[] freshBytes = await fresh.Content.ReadAsByteArrayAsync();
        using var freshJson = JsonDocument.Parse(freshBytes);
        var stored = await fixture.StoredAsync("countries", id);
        Assert.Equal(Code(stored, "Iso2"), Code(freshJson.RootElement, "Iso2"));
        Assert.Equal(Code(stored, "Iso3"), Code(freshJson.RootElement, "Iso3"));

        // Country creation currently appends a row; Accounting replay semantics do not apply here.
        using var repeated = await client.PostAsJsonAsync("/Countries", request);
        Assert.Equal(HttpStatusCode.Created, repeated.StatusCode);
        byte[] repeatedBytes = await repeated.Content.ReadAsByteArrayAsync();
        using var repeatedJson = JsonDocument.Parse(repeatedBytes);
        Assert.NotEqual(id, repeatedJson.RootElement.GetProperty("Id").GetInt32());

        using var updated = await client.PutAsJsonAsync($"/Countries/{id}", request);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        using var after = await client.GetAsync($"/Countries/{id}");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        byte[] afterBytes = await after.Content.ReadAsByteArrayAsync();
        using var afterJson = JsonDocument.Parse(afterBytes);
        var afterStored = await fixture.StoredAsync("countries", id);
        Assert.Equal(Code(freshJson.RootElement, "Iso2"), Code(afterJson.RootElement, "Iso2"));
        Assert.Equal(Code(freshJson.RootElement, "Iso3"), Code(afterJson.RootElement, "Iso3"));
        Assert.Equal(Code(afterStored, "Iso2"), Code(afterJson.RootElement, "Iso2"));
        Assert.Equal(Code(afterStored, "Iso3"), Code(afterJson.RootElement, "Iso3"));

        await using var country = fixture.CreateCountryContext();
        await using var catalog = fixture.CreateCatalogContext();
        await using var currency = fixture.CreateCurrencyContext();
        Assert.Equal(2, await country.Countries.CountAsync());
        Assert.Equal(0, await catalog.Countries.CountAsync());
        Assert.Equal(0, await currency.Currencies.CountAsync());
        var model = country.Model.FindEntityType(typeof(Legacy.Maliev.CatalogService.Domain.Country))!;

        Export(caseName + "-created.json", createdBytes);
        Export(caseName + "-fresh.json", freshBytes);
        Export(caseName + "-repeated.json", repeatedBytes);
        Export(caseName + "-after-put.json", afterBytes);
        Export(caseName + "-after-db.json", JsonSerializer.SerializeToUtf8Bytes(afterStored));
        Export(caseName + "-observation.json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            SyntheticOnly = true,
            DiagnosticOnly = true,
            SourceLiteralParityObserved = iso2 == Code(freshJson.RootElement, "Iso2")
                && iso3 == Code(freshJson.RootElement, "Iso3"),
            RequestedIso2 = iso2,
            RequestedIso3 = iso3,
            StoredIso2 = Code(stored, "Iso2"),
            StoredIso3 = Code(stored, "Iso3"),
            Iso2ColumnType = model.FindProperty("Iso2")!.GetColumnType(),
            Iso3ColumnType = model.FindProperty("Iso3")!.GetColumnType(),
            CountryRows = 2,
            CatalogCountryRows = 0,
            CurrencyRows = 0,
            ActualAuthProducerJoinProven = false,
        }));
        // Keep the real source-parity RED after retaining all original diagnostic evidence.
        Assert.Equal(iso2, Code(freshJson.RootElement, "Iso2"));
        Assert.Equal(iso3, Code(freshJson.RootElement, "Iso3"));
        Assert.Equal(iso2, Code(afterStored, "Iso2"));
        Assert.Equal(iso3, Code(afterStored, "Iso3"));
    }

    [Theory]
    [InlineData("liveness", "/catalog/liveness", true)]
    [InlineData("readiness", "/catalog/readiness", true)]
    [InlineData("old-material-prefix", "/materials/liveness", false)]
    public async Task NormalAnonymousHealthUsesCatalogPrefixWithoutAddingLegacyAliases(
        string caseName, string path, bool healthy)
    {
        using var client = fixture.CreateAnonymousClient();
        using var response = await client.GetAsync(path);
        if (healthy) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Export("health-" + caseName + "-body.bin", await response.Content.ReadAsByteArrayAsync());
        Export("health-" + caseName + "-observation.json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            SyntheticOnly = true,
            DiagnosticOnly = true,
            Path = path,
            Status = (int)response.StatusCode,
            ExpectedHealthyCatalogEndpoint = healthy,
            SlowStartupBudgetOrDeployedRolloutProven = false,
        }));
    }

    // Normal Program intentionally omits null properties through WhenWritingNull.
    // Missing and explicit null both represent the nullable DTO value for this literal comparison.
    private static string? Code(JsonElement value, string name) =>
        value.TryGetProperty(name, out var field) && field.ValueKind != JsonValueKind.Null ? field.GetString() : null;

    private static void Export(string name, byte[] bytes)
    {
        string? folder = Environment.GetEnvironmentVariable("CATALOG_SOURCE_DIAGNOSTIC_OUTPUT_DIRECTORY");
        if (folder is null) return;
        string? results = Environment.GetEnvironmentVariable("VSTestResultsDirectory");
        string? workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!Path.IsPathFullyQualified(folder) || results is null || !Path.IsPathFullyQualified(results)
            || workspace is null || !Path.IsPathFullyQualified(workspace) || bytes.Length > 16 * 1024)
            throw new InvalidOperationException("Require a bounded export in an explicitly owned absolute results directory.");
        folder = Path.GetFullPath(folder);
        results = Path.GetFullPath(results);
        workspace = Path.GetFullPath(workspace);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(folder, Path.Combine(results, "country-health-diagnostics"), comparison)
            || !string.Equals(Path.GetDirectoryName(results), workspace, comparison)
            || Path.GetFileName(results) is not ("runner-results" or "country-health-diagnostic-results")
            || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(folder, name))), folder, comparison))
            throw new InvalidOperationException("Exports must remain in the exact current hosted run results subdirectory.");
        RejectReparseAncestors(folder);
        Directory.CreateDirectory(folder);
        RejectReparseAncestors(folder);
        using var stream = new FileStream(Path.Combine(folder, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }

    private static void RejectReparseAncestors(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Owned diagnostic exports must not traverse symlink or reparse ancestors.");
    }
}
