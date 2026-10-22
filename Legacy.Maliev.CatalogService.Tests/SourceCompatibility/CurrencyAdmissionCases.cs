using System.Net.Http.Headers;
using System.Text;

namespace Legacy.Maliev.CatalogService.Tests.SourceCompatibility;

internal sealed record CurrencyAdmissionCase(
    string Name, string Body, int SourceStatus, int TargetStatus,
    string? ShortName = "THB", string? LongName = "Thai Baht", int SourceId = 0);

internal static class CurrencyAdmissionCases
{
    internal static readonly IReadOnlyList<CurrencyAdmissionCase> All = Build();
    internal static readonly string[] Encodings = ["utf-8", "utf-16"];

    internal static CurrencyAdmissionCase Find(string name) => All.Single(item => item.Name == name);

    internal static ByteArrayContent Content(string body, string encoding)
    {
        var codec = encoding == "utf-16" ? Encoding.Unicode : Encoding.UTF8;
        var content = new ByteArrayContent(codec.GetBytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = encoding };
        return content;
    }

    private static IReadOnlyList<CurrencyAdmissionCase> Build()
    {
        const string names = @"""ShortName"":""THB"",""LongName"":""Thai Baht""";
        var cases = new List<CurrencyAdmissionCase>
        {
            new("ordinary", "{" + names + "}", 200, 200),
            new("empty-names", @"{""ShortName"":"""",""LongName"":""""}", 200, 200, "", ""),
            new("literal-whitespace", "{\"ShortName\":\" \\t \",\"LongName\":\"  Baht  \"}", 200, 200, " \t ", "  Baht  "),
            new("thai-names", @"{""ShortName"":""บาท"",""LongName"":""เงินบาท""}", 200, 200, "บาท", "เงินบาท"),
            new("integer-names", @"{""ShortName"":123,""LongName"":-456}", 200, 200, "123", "-456"),
            new("boolean-names", @"{""ShortName"":true,""LongName"":false}", 200, 200, "true", "false"),
            new("case-insensitive", @"{""shortname"":""THB"",""LONGNAME"":""Thai Baht""}", 200, 200),
            new("escaped-property-name", @"{""Short\u004eame"":""THB"",""LongName"":""Thai Baht""}", 200, 200),
            new("short-valid-then-null", "{" + names + @",""ShortName"":null}", 200, 200),
            new("long-valid-then-null", "{" + names + @",""LongName"":null}", 200, 200),
            new("case-variant-null-duplicate", "{" + names + @",""shortname"":null,""LONGNAME"":null}", 200, 200),
            new("null-then-valid", @"{""ShortName"":null,""LongName"":null," + names + "}", 200, 200),
            new("last-nonnull-wins", "{" + names + @",""ShortName"":""USD"",""LongName"":""US Dollar""}", 200, 200, "USD", "US Dollar"),
            new("id-and-utc-dates", "{" + names + @",""Id"":777777,""CreatedDate"":""2000-01-01T01:02:03Z"",""ModifiedDate"":""2001-01-01T01:02:03Z""}", 200, 200, SourceId: 777777),
            new("quoted-native-id", "{" + names + @",""iD"":"" +0007 ""}", 200, 200, SourceId: 7),
            new("negative-native-id", "{" + names + @",""Id"":-7}", 200, 200, SourceId: -7),
            new("int-minimum", "{" + names + @",""Id"":-2147483648}", 200, 200, SourceId: int.MinValue),
            new("int-maximum", "{" + names + @",""Id"":2147483647}", 200, 200, SourceId: int.MaxValue),
            new("null-native-id", "{" + names + @",""Id"":null}", 200, 200),
            new("empty-native-id", "{" + names + @",""Id"":""""}", 200, 200),
            new("id-valid-then-null", "{" + names + @",""Id"":7,""Id"":null}", 200, 200, SourceId: 7),
            new("nullable-dates-empty", "{" + names + @",""CreatedDate"":"""",""ModifiedDate"":null}", 200, 200),
            new("native-date-forms", "{" + names + @",""CreatedDate"":""/Date(0)/"",""ModifiedDate"":""2001-01-01T01:02:03+07:00""}", 200, 200),
            new("dates-valid-then-null", "{" + names + @",""CreatedDate"":""2000-01-01"",""CreatedDate"":null,""ModifiedDate"":""2001-01-01"",""ModifiedDate"":null}", 200, 200),
            new("unknown-object", "{" + names + @",""Opaque"":{""Id"":[],""CreatedDate"":{}}}", 200, 200),
            new("leading-id-metadata", @"{""$id"":""currency""," + names + "}", 200, 200),
            new("leading-type-not-activated", @"{""$type"":""Missing.Type, Missing.Assembly""," + names + "}", 200, 200),
            new("leading-null-reference", @"{""$ref"":null," + names + "}", 200, 200),
            new("late-invalid-reference-is-unknown", "{" + names + @",""$ref"":1}", 200, 200),
            new("wrong-case-reference-is-unknown", @"{""$REF"":1," + names + "}", 200, 200),
            new("unknown-stops-metadata-prefix", @"{""Opaque"":0,""$ref"":1," + names + "}", 200, 200),
            new("leading-numeric-reference", @"{""$ref"":1," + names + "}", 400, 400),
            new("leading-array-reference", @"{""$ref"":[]," + names + "}", 400, 400),
            new("unresolved-reference-only", @"{""$ref"":""missing""}", 400, 400),
            new("reference-with-extra-properties", @"{""$ref"":""missing""," + names + "}", 400, 400),
            new("leading-values-array", @"{""$values"":[]," + names + "}", 400, 400),
            new("metadata-chain-reference", @"{""$id"":""currency"",""$type"":""Ignored.Type"",""$ref"":1," + names + "}", 400, 400),
            new("single-quoted-grammar", "{'ShortName':'THB','LongName':'Thai Baht'}", 200, 200),
            new("unquoted-member-grammar", "{ShortName:\"THB\",LongName:\"Thai Baht\"}", 200, 200),
            new("leading-comment-grammar", "/* currency */{" + names + "}", 200, 200),
            new("embedded-comment-grammar", "{/* currency */" + names + "}", 200, 200),
            new("trailing-comma-grammar", "{" + names + ",}", 200, 200),
            new("trailing-second-object", "{" + names + "}{}", 200, 200),
            new("trailing-unread-text", "{" + names + "} ignored", 200, 200),
            new("null-root", "null", 400, 400),
            new("array-root", "[]", 400, 400),
            new("string-root", @"""THB""", 400, 400),
            new("integer-root", "7", 400, 400),
            new("empty-body", "", 400, 400),
            new("truncated-object", "{\"ShortName\":", 400, 400),
            new("missing-separator", @"{""ShortName"":""THB"" ""LongName"":""Thai Baht""}", 400, 400),
            // Deliberately retained migration guards: the original POCO has no annotations.
            new("required-short-null", @"{""ShortName"":null,""LongName"":""Thai Baht""}", 200, 400, null),
            new("required-long-null", @"{""ShortName"":""THB"",""LongName"":null}", 200, 400, LongName: null),
            new("required-short-omitted", @"{""LongName"":""Thai Baht""}", 200, 400, null),
            new("required-long-omitted", @"{""ShortName"":""THB""}", 200, 400, LongName: null),
            new("both-omitted", "{}", 200, 400, null, null),
            new("short-exact-maximum", @"{""ShortName"":""1234567890"",""LongName"":""Thai Baht""}", 200, 200, "1234567890"),
            new("short-over-maximum", @"{""ShortName"":""12345678901"",""LongName"":""Thai Baht""}", 200, 400, "12345678901"),
            new("long-exact-maximum", "{\"ShortName\":\"THB\",\"LongName\":\"" + new string('x', 50) + "\"}", 200, 200, LongName: new string('x', 50)),
            new("long-over-maximum", "{\"ShortName\":\"THB\",\"LongName\":\"" + new string('x', 51) + "\"}", 200, 400, LongName: new string('x', 51)),
        };
        foreach (var field in new[] { "ShortName", "LongName" })
        {
            foreach (var token in new[] { "[]", "{}" })
            {
                cases.Add(new($"{field}-structured-{token}", "{" + names + ",\"" + field + "\":" + token + "}", 400, 400));
                cases.Add(new($"{field}-invalid-before-valid-{token}", "{\"" + field + "\":" + token + "," + names + "}", 400, 400));
            }
        }
        foreach (var token in new[] { "[]", "{}", "true", "2147483648", "-2147483649", "1.5", "\"1.5\"", "\"invalid\"" })
        {
            cases.Add(new($"id-malformed-{token}", "{" + names + ",\"Id\":" + token + "}", 400, 400));
            cases.Add(new($"id-invalid-before-valid-{token}", "{" + names + ",\"Id\":" + token + ",\"Id\":7}", 400, 400));
        }
        foreach (var field in new[] { "CreatedDate", "ModifiedDate" })
        {
            foreach (var token in new[] { "[]", "{}", "123", "true", "\"invalid-date\"" })
            {
                cases.Add(new($"{field}-malformed-{token}", "{" + names + ",\"" + field + "\":" + token + "}", 400, 400));
                cases.Add(new($"{field}-invalid-before-valid-{token}", "{" + names + ",\"" + field + "\":" + token + ",\"" + field + "\":\"2000-01-01\"}", 400, 400));
            }
        }
        cases.Add(new("unknown-at-source-depth-limit", "{" + names + ",\"Opaque\":" + new string('[', 31) + "0" + new string(']', 31) + "}", 200, 200));
        cases.Add(new("unknown-over-source-depth-limit", "{" + names + ",\"Opaque\":" + new string('[', 32) + "0" + new string(']', 32) + "}", 400, 400));
        return cases;
    }
}
