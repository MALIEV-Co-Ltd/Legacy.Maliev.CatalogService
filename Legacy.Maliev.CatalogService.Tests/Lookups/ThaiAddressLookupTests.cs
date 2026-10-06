using Legacy.Maliev.CatalogService.Api.Lookups;
using Legacy.Maliev.CatalogService.Application.Lookups;

namespace Legacy.Maliev.CatalogService.Tests.Lookups;

public sealed class ThaiAddressLookupTests
{
    internal static string ApiRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Legacy.Maliev.CatalogService.slnx")))
            root = root.Parent;
        return Path.Combine(root!.FullName, "Legacy.Maliev.CatalogService.Api");
    }

    private static ThaiAddressLookup Dataset() => ThaiAddressDataset.Load(ApiRoot());
    [Fact]
    public void Licensed_snapshot_has_full_hierarchy_and_known_independent_combination()
    {
        var lookup = Dataset();
        Assert.Equal(ThaiAddressDataset.Version, lookup.DatasetVersion);
        var provinces = lookup.Areas("provinces", new() { Limit = 50 });
        Assert.Equal(50, provinces.Items.Count);
        Assert.True(provinces.HasMore);
        Assert.Equal(27, lookup.Areas("provinces", new() { Limit = 50, Cursor = provinces.NextCursor }).Items.Count);
        var row = Assert.Single(lookup.Search(new() { SubdistrictCode = "120610" }).Items);
        Assert.Equal("นนทบุรี", row.Province.NameTh);
        Assert.Equal("1206", row.District.Code);
        Assert.Equal("คลองข่อย", row.Subdistrict.NameTh);
        Assert.Equal("11120", row.Postcode);
    }

    [Theory]
    [InlineData("คลองข่อย")]
    [InlineData("Khlong Khoi")]
    public void Duplicate_names_remain_ambiguous_until_parent_constraint(string q)
    {
        var lookup = Dataset();
        Assert.Equal(2, lookup.Search(new() { Q = q }).Items.Count);
        var row = Assert.Single(lookup.Search(new() { Q = q, ProvinceCode = "12", DistrictCode = "1206", Postcode = "๑๑๑๒๐" }).Items);
        Assert.Equal("120610", row.Subdistrict.Code);
        Assert.Empty(lookup.Search(new() { Q = q, ProvinceCode = "12", Postcode = "70120" }).Items);
    }

    [Theory]
    [InlineData("กรุงเทพฯ")]
    [InlineData("กทม.")]
    [InlineData("Bangkok")]
    public void Bangkok_aliases_narrow_to_province_ten(string q) => Assert.All(Dataset().Search(new() { Q = q }).Items, row => Assert.Equal("10", row.Province.Code));

    [Theory]
    [InlineData("ต.คลองข่อย\tอ.ปากเกร็ด จ.นนทบุรี")]
    [InlineData("Tambon Khlong Khoi\nDistrict Pak Kret Province Nonthaburi")]
    public void Autocomplete_accepts_administrative_labels_and_whitespace_variants(string q)
    {
        var row = Assert.Single(Dataset().Search(new() { Q = q }).Items);
        Assert.Equal("120610", row.Subdistrict.Code);
    }

    [Fact]
    public void Bangkok_district_prefix_is_supported_in_autocomplete()
    {
        var page = Dataset().Search(new() { Q = "เขตพระนคร", ProvinceCode = "10" });
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, row => Assert.Equal("1001", row.District.Code));
    }
    [Fact]
    public void Postcode_first_returns_every_match_across_pages_and_cursor_is_query_bound()
    {
        var lookup = Dataset();
        var first = lookup.Search(new() { Postcode = "11120", Limit = 1 });
        Assert.True(first.HasMore);
        var codes = new HashSet<string>
        {
            first.Items[0].Subdistrict.Code
        };
        while (first.HasMore)
        {
            first = lookup.Search(new() { Postcode = "11120", Limit = 1, Cursor = first.NextCursor });
            Assert.True(codes.Add(Assert.Single(first.Items).Subdistrict.Code));
        }

        Assert.Contains("120610", codes);
        Assert.True(codes.Count > 1);
        var cursor = lookup.Search(new() { Postcode = "11120", Limit = 1 }).NextCursor;
        Assert.Throws<ArgumentException>(() => lookup.Search(new() { Postcode = "70120", Limit = 1, Cursor = cursor }));
        Assert.Throws<ArgumentException>(() => lookup.Search(new() { Cursor = "bogus" }));
    }

    [Fact]
    public void Supports_multiple_postcodes_per_subdistrict_but_rejects_corrupt_hierarchy()
    {
        var row = Assert.Single(Dataset().Search(new() { SubdistrictCode = "120610" }).Items);
        var lookup = new ThaiAddressLookup("fixture", [row, row with { Postcode = "11121" }]);
        Assert.Equal(2, lookup.Search(new() { SubdistrictCode = "120610" }).Items.Count);
        Assert.Equal(new[] { "11120", "11121" }, lookup.Postcodes(new()).Items);
        Assert.Throws<InvalidDataException>(() => new ThaiAddressLookup("bad", [row, row]));
        Assert.Throws<InvalidDataException>(() => new ThaiAddressLookup("bad", [row with { District = row.District with { ParentCode = "70" } }]));
        Assert.Throws<InvalidDataException>(() => new ThaiAddressLookup("bad", [row with { Postcode = "x" }]));
    }

    [Fact]
    public void Lists_apply_all_constraints_and_empty_match_is_explicit()
    {
        var lookup = Dataset();
        Assert.Equal("1206", Assert.Single(lookup.Areas("districts", new() { ProvinceCode = "12", DistrictCode = "1206" }).Items).Code);
        Assert.Equal("120610", Assert.Single(lookup.Areas("subdistricts", new() { SubdistrictCode = "120610" }).Items).Code);
        Assert.Equal("11120", Assert.Single(lookup.Postcodes(new() { SubdistrictCode = "120610" }).Items));
        Assert.Empty(lookup.Search(new() { Q = "unrelated-nonexistent-place" }).Items);
        Assert.Throws<ArgumentException>(() => lookup.Areas("invalid", new()));
        Assert.Throws<ArgumentException>(() => lookup.Search(new() { ProvinceCode = "70", DistrictCode = "1206" }));
        Assert.Throws<ArgumentException>(() => lookup.Search(new() { ProvinceCode = "70", SubdistrictCode = "120610" }));
        Assert.Throws<ArgumentException>(() => lookup.Search(new() { SubdistrictCode = "000000" }));
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(51, null, null)]
    [InlineData(20, "1112x", null)]
    [InlineData(20, null, "xx")]
    public void Rejects_invalid_inputs(int limit, string? postcode, string? province) => Assert.Throws<ArgumentException>(() => Dataset().Search(new() { Limit = limit, Postcode = postcode, ProvinceCode = province }));
    [Theory]
    [InlineData("36/1 หมู่ 3 ต.คลองข่อย อ.ปากเกร็ด จ.นนทบุรี 11120", "36/1 หมู่ 3")]
    [InlineData("36/1 หมู่ ๓ ต.คลองข่อย อ.ปากเกร็ด จ.นนทบุรี ๑๑๑๒๐", "36/1 หมู่ ๓")]
    [InlineData("36/1 Tambon Khlong Khoi Amphoe Pak Kret Province Nonthaburi 11120", "36/1")]
    [InlineData("36/1 Khlong Khoi Subdistrict Pak Kret District Nonthaburi Province 11120", "36/1")]
    [InlineData("36/1 ต . คลองข่อย อ . ปากเกร็ด จ . นนทบุรี 11120", "36/1")]
    [InlineData("36/1 แขวงพระบรมมหาราชวัง เขตพระนคร กรุงเทพฯ 10200", "36/1")]
    public void Resolves_explicit_components_without_losing_house_moo_or_original(string text, string detail)
    {
        var result = Dataset().Resolve(new() { Text = text });
        Assert.Equal("exact", result.Outcome);
        Assert.Equal(text, result.OriginalText);
        Assert.Equal(detail, result.DetailText);
        Assert.NotNull(result.UniqueFields.Subdistrict);
        Assert.NotNull(result.UniqueFields.Postcode);
        Assert.All(result.ExtractedSpans, s => Assert.Equal(s.Text, text.Substring(s.Start, s.Length)));
    }

    [Fact]
    public void Ambiguity_preserves_building_road_and_complete_set_uniqueness()
    {
        var lookup = Dataset();
        var duplicate = lookup.Resolve(new() { Text = "36/1 อาคาร A ถนนคลองข่อย ต.คลองข่อย" });
        Assert.Equal("ambiguous", duplicate.Outcome);
        Assert.Null(duplicate.UniqueFields.Province);
        Assert.Null(duplicate.UniqueFields.Subdistrict);
        Assert.Null(duplicate.UniqueFields.Postcode);
        Assert.Equal("36/1 อาคาร A ถนนคลองข่อย", duplicate.DetailText);
        var large = lookup.Resolve(new() { Text = "Province Bangkok" });
        Assert.True(large.HasMore);
        Assert.Equal("10", large.UniqueFields.Province?.Code);
        Assert.Null(large.UniqueFields.District);
        Assert.Null(large.UniqueFields.Subdistrict);
        Assert.Null(large.UniqueFields.Postcode);
    }

    [Theory]
    [InlineData("ต.คลองข่อย อ.ปากเกร็ด จ.นนทบุรี 70120")]
    [InlineData("ต.คลองข่อย จ.นนทบุรี จ.ราชบุรี")]
    [InlineData("ต.ไม่มีสถานที่นี้ จ.นนทบุรี")]
    public void Conflicting_or_unknown_explicit_components_are_not_silently_autofilled(string text)
    {
        var result = Dataset().Resolve(new() { Text = text });
        Assert.Equal("conflict", result.Outcome);
        Assert.Empty(result.Candidates);
        Assert.Empty(result.ExtractedSpans);
        Assert.NotEmpty(result.Conflicts);
        Assert.Equal(text, result.DetailText);
    }

    [Fact]
    public void Entered_constraints_cannot_be_overwritten_and_missing_postcode_is_unknown()
    {
        var lookup = Dataset();
        Assert.Equal("conflict", lookup.Resolve(new() { Text = "ต.คลองข่อย จ.นนทบุรี", Constraints = new() { ProvinceCode = "70" } }).Outcome);
        var missing = lookup.Resolve(new() { Text = "ต.คลองข่อย อ.ปากเกร็ด จ.นนทบุรี" });
        Assert.Equal("exact", missing.Outcome);
        Assert.Null(missing.UniqueFields.Postcode);
        Assert.Throws<ArgumentException>(() => lookup.Resolve(new() { Text = "x", Constraints = new() { Q = "x" } }));
    }

    [Theory]
    [InlineData("unrelated house and road text")]
    [InlineData("36/1 หมู่ 3 ถนนคลองข่อย")]
    [InlineData("36/1 Nonthaburi Road")]
    [InlineData("36/1 ถนน นนทบุรี")]
    [InlineData("36/1 อาคาร นนทบุรี")]
    [InlineData("Unit 11120")]
    [InlineData("บ้านเลขที่ 11120")]
    [InlineData("  36/1 ถนนคลองข่อย  ")]
    public void Unrelated_and_road_only_text_is_preserved_without_false_place_match(string text)
    {
        var result = Dataset().Resolve(new() { Text = text });
        Assert.Equal("not-found", result.Outcome);
        Assert.Equal(text, result.DetailText);
    }

    [Fact]
    public void Invalid_text_and_missing_snapshot_fail_safely()
    {
        var lookup = Dataset();
        Assert.Throws<ArgumentException>(() => lookup.Resolve(new() { Text = " " }));
        Assert.Throws<ArgumentException>(() => lookup.Resolve(new() { Text = new string('x', 2049) }));
        Assert.Throws<ArgumentException>(() => lookup.Resolve(new() { Text = "a\0b" }));
        Assert.Throws<ArgumentException>(() => lookup.Search(new() { Q = new string('x', 129) }));
        Assert.Throws<InvalidOperationException>(() => ThaiAddressDataset.Load(Path.Combine(ApiRoot(), "missing")).Search(new()));
    }
}
