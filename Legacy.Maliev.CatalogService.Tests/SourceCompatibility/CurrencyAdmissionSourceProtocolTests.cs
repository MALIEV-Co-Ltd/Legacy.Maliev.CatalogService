using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Legacy.Maliev.CatalogService.Tests.SourceCompatibility;

public sealed class CurrencyAdmissionSourceProtocolTests(CurrencyAdmissionSourceHost source)
    : IClassFixture<CurrencyAdmissionSourceHost>
{
    public static TheoryData<string, string> Cases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var item in CurrencyAdmissionCases.All)
            {
                foreach (var encoding in CurrencyAdmissionCases.Encodings)
                {
                    data.Add(item.Name, encoding);
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task StockSourceFormatter_CharacterizesFiveFieldAdmission(string name, string encoding)
    {
        var item = CurrencyAdmissionCases.Find(name);
        using var content = CurrencyAdmissionCases.Content(item.Body, encoding);
        using var response = await source.Client.PostAsync("/currency-source-oracle", content);
        Assert.Equal((HttpStatusCode)item.SourceStatus, response.StatusCode);
        if (item.SourceStatus != 200)
        {
            return;
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var model = document.RootElement;
        Assert.Equal(5, model.EnumerateObject().Count());
        Assert.Equal(item.SourceId, model.GetProperty("Id").GetInt32());
        Assert.Equal(item.ShortName, model.GetProperty("ShortName").GetString());
        Assert.Equal(item.LongName, model.GetProperty("LongName").GetString());
        if (name == "id-and-utc-dates")
        {
            Assert.Equal(new DateTime(2000, 1, 1, 1, 2, 3, DateTimeKind.Utc), model.GetProperty("CreatedDate").GetDateTime());
            Assert.Equal(new DateTime(2001, 1, 1, 1, 2, 3, DateTimeKind.Utc), model.GetProperty("ModifiedDate").GetDateTime());
        }
        if (name == "dates-valid-then-null")
        {
            Assert.Equal(new DateTime(2000, 1, 1), model.GetProperty("CreatedDate").GetDateTime());
            Assert.Equal(new DateTime(2001, 1, 1), model.GetProperty("ModifiedDate").GetDateTime());
        }
        if (name == "nullable-dates-empty")
        {
            Assert.Equal(JsonValueKind.Null, model.GetProperty("CreatedDate").ValueKind);
            Assert.Equal(JsonValueKind.Null, model.GetProperty("ModifiedDate").ValueKind);
        }
    }

    [Theory]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("iso-8859-1")]
    public async Task SourceFormatter_RejectsUnsupportedCharset(string charset)
    {
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes("{}"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = charset };
        using var response = await source.Client.PostAsync("/currency-source-oracle", content);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }
}
