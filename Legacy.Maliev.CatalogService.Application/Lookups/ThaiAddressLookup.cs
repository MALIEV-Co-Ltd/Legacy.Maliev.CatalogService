using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Legacy.Maliev.CatalogService.Application.Lookups;

/// <summary>Immutable local hierarchy with deterministic AND filtering and conservative extraction.</summary>
public sealed class ThaiAddressLookup
{
    private readonly AddressCombination[] rows;
    private readonly (string Kind, Regex Names, Regex Prefixes, Dictionary<string, HashSet<string>> Codes)[] components;
    /// <summary>Immutable source version; empty means unavailable.</summary>
    public string DatasetVersion { get; }

    /// <summary>Validates relationships, codes and names before exposing a dataset.</summary>
    public ThaiAddressLookup(string datasetVersion, IEnumerable<AddressCombination> combinations)
    {
        DatasetVersion = datasetVersion;
        rows = combinations.OrderBy(x => x.Subdistrict.Code, StringComparer.Ordinal).ThenBy(x => x.Postcode, StringComparer.Ordinal).ToArray();
        foreach (var row in rows)
        {
            if (!Digits(row.Province.Code, 2) || !Digits(row.District.Code, 4) || !Digits(row.Subdistrict.Code, 6) || !Digits(row.Postcode, 5) || row.District.ParentCode != row.Province.Code || row.Subdistrict.ParentCode != row.District.Code || !row.District.Code.StartsWith(row.Province.Code, StringComparison.Ordinal) || !row.Subdistrict.Code.StartsWith(row.District.Code, StringComparison.Ordinal) || new[]
            {
                row.Province,
                row.District,
                row.Subdistrict
            }.Any(x => string.IsNullOrWhiteSpace(x.NameTh) || string.IsNullOrWhiteSpace(x.NameEn)))
                throw new InvalidDataException("Invalid Thai hierarchy dataset.");
        }

        if (rows.DistinctBy(x => (x.Subdistrict.Code, x.Postcode)).Count() != rows.Length || rows.Select(x => x.Province).GroupBy(x => x.Code).Any(g => g.Distinct().Count() != 1) || rows.Select(x => x.District).GroupBy(x => x.Code).Any(g => g.Distinct().Count() != 1) || rows.Select(x => x.Subdistrict).GroupBy(x => x.Code).Any(g => g.Distinct().Count() != 1))
            throw new InvalidDataException("Duplicate or conflicting Thai hierarchy entries.");
        components = new[]
        {
            (Kind: "province", Prefix: @"(?:จ\s*\.|จังหวัด|Province\s+)", Suffix: "Province", Areas: rows.Select(x => x.Province)),
            (Kind: "district", Prefix: @"(?:อ\s*\.|อำเภอ|เขต|Amphoe\s+|District\s+)", Suffix: "(?:Amphoe|District)", Areas: rows.Select(x => x.District)),
            (Kind: "subdistrict", Prefix: @"(?:ต\s*\.|ตำบล|แขวง|Tambon\s+|Subdistrict\s+)", Suffix: "(?:Tambon|Subdistrict)", Areas: rows.Select(x => x.Subdistrict))
        }.Select(level =>
        {
            var names = level.Areas.Distinct().SelectMany(a => Aliases(a).Select(n => (Area: a, Name: n))).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Key.Length).ToArray();
            var alternatives = string.Join('|', names.Select(g => Regex.Escape(g.Key).Replace("\\ ", @"\s+", StringComparison.Ordinal)));
            return (level.Kind,
                new Regex($@"(?<![\p{{L}}\p{{M}}])(?:{level.Prefix}\s*(?<name>{alternatives})|(?<name>{alternatives})\s+{level.Suffix})(?![\p{{L}}\p{{M}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                new Regex($@"(?<![\p{{L}}\p{{M}}])(?:{level.Prefix}|{level.Suffix}(?![\p{{L}}\p{{M}}]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                names.ToDictionary(g => g.Key, g => g.Select(x => x.Area.Code).ToHashSet(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase));
        }).ToArray();
    }

    /// <summary>Returns all matching combinations through bounded pages.</summary>
    public LookupPage<AddressCombination> Search(AddressQuery query) => Page(Filter(query), query, "autocomplete");
    /// <summary>Returns distinct administrative entities, with the same constraints and paging.</summary>
    public LookupPage<AdministrativeArea> Areas(string level, AddressQuery query)
    {
        var matches = Filter(query);
        var areas = level switch
        {
            "provinces" => matches.Select(x => x.Province),
            "districts" => matches.Select(x => x.District),
            "subdistricts" => matches.Select(x => x.Subdistrict),
            _ => throw new ArgumentException("Invalid administrative level.")
        };
        return Page(areas.DistinctBy(x => x.Code).OrderBy(x => x.Code, StringComparer.Ordinal), query, level);
    }

    /// <summary>Returns distinct postcodes without claiming a unique administrative match.</summary>
    public LookupPage<string> Postcodes(AddressQuery query) => Page(Filter(query).Select(x => x.Postcode).Distinct().Order(StringComparer.Ordinal), query, "postcodes");
    private IEnumerable<AddressCombination> Filter(AddressQuery query)
    {
        Validate(query);
        var postcode = query.Postcode is null ? null : NormalizeDigits(query.Postcode);
        var namesOnly = Regex.Replace(query.Q ?? "", @"(?<![\p{L}\p{M}])(?:จ\s*\.|อ\s*\.|ต\s*\.|จังหวัด|อำเภอ|ตำบล|แขวง|เขต|Province\s+|District\s+|Subdistrict\s+|Amphoe\s+|Tambon\s+)\s*", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var tokens = Regex.Split(namesOnly.Trim(), @"\s+").Where(t => t.Length > 0).ToArray();
        return rows.Where(x => (query.ProvinceCode is null || x.Province.Code == query.ProvinceCode) && (query.DistrictCode is null || x.District.Code == query.DistrictCode) && (query.SubdistrictCode is null || x.Subdistrict.Code == query.SubdistrictCode) && (postcode is null || x.Postcode == postcode) && tokens.All(t => new[] { x.Province, x.District, x.Subdistrict }.SelectMany(Aliases).Append(x.Postcode).Any(n => n.Contains(NormalizeDigits(t), StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>Checks bounded inputs and parent constraints independently of model binding.</summary>
    public void Validate(AddressQuery query)
    {
        if (rows.Length == 0 || string.IsNullOrEmpty(DatasetVersion))
            throw new InvalidOperationException("Thai address dataset unavailable.");
        if (query.Limit is < 1 or > 50 || query.Q?.Length > 128 || query.Cursor?.Length > 512 || query.ProvinceCode is not null && !Digits(query.ProvinceCode, 2) || query.DistrictCode is not null && !Digits(query.DistrictCode, 4) || query.SubdistrictCode is not null && !Digits(query.SubdistrictCode, 6) || query.Postcode is not null && !Digits(NormalizeDigits(query.Postcode), 5))
            throw new ArgumentException("Invalid address lookup input.");
        if (query.ProvinceCode is not null && !rows.Any(x => x.Province.Code == query.ProvinceCode) || query.DistrictCode is not null && !rows.Any(x => x.District.Code == query.DistrictCode && (query.ProvinceCode is null || x.Province.Code == query.ProvinceCode)) || query.SubdistrictCode is not null && !rows.Any(x => x.Subdistrict.Code == query.SubdistrictCode && (query.DistrictCode is null || x.District.Code == query.DistrictCode) && (query.ProvinceCode is null || x.Province.Code == query.ProvinceCode)))
            throw new ArgumentException("Unknown or inconsistent administrative codes.");
    }

    private LookupPage<T> Page<T>(IEnumerable<T> values, AddressQuery query, string route)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', DatasetVersion, route, query.Q, query.ProvinceCode, query.DistrictCode, query.SubdistrictCode, query.Postcode, query.Limit.ToString(CultureInfo.InvariantCulture)))));
        var offset = 0;
        if (query.Cursor is not null)
        {
            var parts = query.Cursor.Split(':');
            if (parts.Length != 2 || parts[0] != fingerprint || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset < 0)
                throw new ArgumentException("Invalid or stale lookup cursor.");
        }

        var all = values.ToArray();
        if (offset > all.Length)
            throw new ArgumentException("Invalid lookup cursor offset.");
        var items = all.Skip(offset).Take(query.Limit).ToArray();
        var more = offset + items.Length < all.Length;
        return new(DatasetVersion, items, more, more ? $"{fingerprint}:{offset + items.Length}" : null);
    }

    /// <summary>Normalizes Thai digits without changing text offsets.</summary>
    public static string NormalizeDigits(string value) => new(value.Select(c => c is >= '๐' and <= '๙' ? (char)('0' + c - '๐') : c).ToArray());
    private static bool Digits(string value, int length) => value.Length == length && value.All(c => c is >= '0' and <= '9');
    private static IEnumerable<string> Aliases(AdministrativeArea area)
    {
        yield return area.NameTh;
        yield return area.NameEn;
        if (area.Code == "10")
        {
            yield return "กรุงเทพฯ";
            yield return "กทม.";
            yield return "กทม";
        }
    }

    /// <summary>Extracts only confident spans and computes uniqueness over every matching combination.</summary>
    public AddressResolution Resolve(ResolveAddressRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 2048 || request.Text.Any(c => char.IsControl(c) && !char.IsWhiteSpace(c)))
            throw new ArgumentException("Invalid address text.");
        var constraints = request.Constraints ?? new AddressQuery();
        Validate(constraints);
        if (constraints.Q is not null || constraints.Cursor is not null)
            throw new ArgumentException("Resolve constraints accept codes and postcode only.");
        var text = NormalizeDigits(request.Text);
        var found = new List<(string Kind, HashSet<string> Codes, AddressSpan Span)>();
        var conflicts = new List<string>();
        foreach (var level in components)
        {
            foreach (Match match in level.Names.Matches(text))
            {
                var name = Regex.Replace(match.Groups["name"].Value, @"\s+", " ");
                var codes = level.Codes[name];
                found.Add((level.Kind, codes, new(match.Index, match.Length, level.Kind, request.Text.Substring(match.Index, match.Length))));
            }

            // Explicit but unknown components must not be silently ignored.
            foreach (Match prefix in level.Prefixes.Matches(text))
                if (!found.Any(f => f.Kind == level.Kind && f.Span.Start <= prefix.Index && f.Span.Start + f.Span.Length > prefix.Index))
                    conflicts.Add(level.Kind);
        }

        // Bare province names are common, including Bangkok; bare district/street names are unsafe to remove.
        if (!found.Any(f => f.Kind == "province"))
        {
            foreach (var province in rows.Select(x => x.Province).Distinct())
                foreach (var name in Aliases(province).OrderByDescending(n => n.Length))
                    foreach (Match match in Regex.Matches(text, $@"(?<![\p{{L}}\p{{M}}]){Regex.Escape(name)}(?![\p{{L}}\p{{M}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                        if (!IsDetailName(text, match.Index, match.Length) && !found.Any(f => f.Kind == "province" && f.Span.Start == match.Index))
                            found.Add(("province", new HashSet<string> { province.Code }, new(match.Index, match.Length, "province", request.Text.Substring(match.Index, match.Length))));
        }

        foreach (Match match in Regex.Matches(text, @"(?<![\p{L}\d/])\d{5}(?=[\s,.]*(?:(?:Thailand|ประเทศไทย)[\s,.]*)?$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            if (!Regex.IsMatch(text[..match.Index], @"(?:บ้านเลขที่|ห้อง|ยูนิต|House|Unit|Room)\s*(?:No\.?\s*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                found.Add(("postcode", new HashSet<string> { match.Value }, new(match.Index, match.Length, "postcode", request.Text.Substring(match.Index, match.Length))));
        bool Matches(AddressCombination row, (string Kind, HashSet<string> Codes, AddressSpan Span) part) => part.Codes.Contains(part.Kind switch
        {
            "province" => row.Province.Code,
            "district" => row.District.Code,
            "subdistrict" => row.Subdistrict.Code,
            _ => row.Postcode
        });
        AddressCombination[] matches = found.Count == 0 ? [] : Filter(constraints).Where(r => found.All(f => Matches(r, f))).ToArray();
        if (found.Count > 0 && matches.Length == 0)
            conflicts.AddRange(found.Select(f => f.Kind));
        var outcome = conflicts.Count > 0 ? "conflict" : matches.Length switch
        {
            0 => "not-found",
            1 => "exact",
            _ => "ambiguous"
        };
        if (outcome == "conflict")
            matches = [];
        AddressSpan[] spans = outcome is "exact" or "ambiguous" ? found.Select(f => f.Span).Distinct().OrderBy(s => s.Start).ToArray() : [];
        var detail = new StringBuilder(request.Text);
        foreach (var span in spans.OrderByDescending(s => s.Start))
            detail.Remove(span.Start, span.Length);
        T? Unique<T>(IEnumerable<T> values)
            where T : class
        {
            var distinct = values.Distinct().Take(2).ToArray();
            return distinct.Length == 1 ? distinct[0] : null;
        }

        return new(DatasetVersion, request.Text, text, outcome, matches.Take(50).ToArray(), matches.Length > 50, new(Unique(matches.Select(r => r.Province)), Unique(matches.Select(r => r.District)), Unique(matches.Select(r => r.Subdistrict)), found.Any(f => f.Kind == "postcode") || constraints.Postcode is not null ? Unique(matches.Select(r => r.Postcode)) : null),
            outcome is "exact" or "ambiguous" ? detail.ToString().Trim() : request.Text, spans, conflicts.Distinct().ToArray());
    }

    private static bool IsDetailName(string text, int start, int length) =>
        Regex.IsMatch(text[..start], @"(?:ถนน|ซอย|อาคาร|หมู่บ้าน|Road|Rd\.?|Street|Soi|Building)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
        || Regex.IsMatch(text[(start + length)..], @"^\s+(?:Road|Rd\.?|Street|Soi|Building)(?![\p{L}\p{M}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
