using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.CatalogService.Application.Models;
using Microsoft.AspNetCore.Mvc.NewtonsoftJson;

namespace Legacy.Maliev.CatalogService.Tests.Controllers;

public sealed class CountryResponseWireContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CountryResponse_UsesConfiguredSourceNamesAndNullOmission_AndExistingReaderDefaults(bool optionalValues)
    {
        var date = optionalValues ? new DateTime(2026, 10, 20, 1, 2, 3, DateTimeKind.Utc) : (DateTime?)null;
        var original = new OriginalCountry
        {
            Id = 7,
            Name = "Thailand",
            Continent = optionalValues ? "Asia" : null,
            CountryCode = optionalValues ? "+66" : null,
            Iso2 = optionalValues ? "TH" : null,
            Iso3 = optionalValues ? "THA" : null,
            CreatedDate = date,
            ModifiedDate = date,
        };
        var sourceSettings = JsonSerializerSettingsProvider.CreateSerializerSettings();
        sourceSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
        sourceSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
        using var expected = JsonDocument.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(original, sourceSettings));
        var target = new CountryResponse(original.Id, original.Name, original.Continent, original.CountryCode,
            original.Iso2, original.Iso3, original.CreatedDate, original.ModifiedDate);
        var targetOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        var json = JsonSerializer.Serialize(target, targetOptions);
        using var actual = JsonDocument.Parse(json);
        var names = optionalValues
            ? new[] { "id", "name", "continent", "countryCode", "iso2", "iso3", "createdDate", "modifiedDate" }
            : new[] { "id", "name" };
        Assert.Equal(names, expected.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(names, actual.RootElement.EnumerateObject().Select(property => property.Name));
        foreach (var property in expected.RootElement.EnumerateObject())
        {
            var value = actual.RootElement.GetProperty(property.Name);
            Assert.Equal(property.Value.ValueKind, value.ValueKind);
            if (property.Name == "id") Assert.Equal(property.Value.GetInt32(), value.GetInt32());
            else if (property.Name.EndsWith("Date", StringComparison.Ordinal))
            {
                Assert.Equal(property.Value.GetDateTime(), value.GetDateTime());
                Assert.Equal(property.Value.GetDateTime().Kind, value.GetDateTime().Kind);
            }
            else Assert.Equal(property.Value.GetString(), value.GetString());
        }

        // Both inspected Web/Intranet country readers use this no-options overload.
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var read = await content.ReadFromJsonAsync<CountryConsumer>();
        Assert.NotNull(read);
        Assert.Equal(7, read.Id);
        Assert.Equal("Thailand", read.Name);

        using var unchanged = JsonDocument.Parse(JsonSerializer.Serialize(new ColorResponse(9, "Control", null, null), targetOptions));
        Assert.True(unchanged.RootElement.TryGetProperty("Id", out _));
        Assert.True(unchanged.RootElement.TryGetProperty("Name", out _));
        Assert.False(unchanged.RootElement.TryGetProperty("id", out _));
    }

    // The eight property names/types match the retained Country entity; no target JSON attributes are copied.
    private sealed class OriginalCountry
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Continent { get; set; }
        public string? CountryCode { get; set; }
        public string? Iso2 { get; set; }
        public string? Iso3 { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    private sealed record CountryConsumer(int Id, string Name);
}
