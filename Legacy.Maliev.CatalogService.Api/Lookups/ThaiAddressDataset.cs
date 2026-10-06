using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Application.Lookups;

namespace Legacy.Maliev.CatalogService.Api.Lookups;
/// <summary>Loads the pinned licensed snapshot; never seeds or modifies a database.</summary>
public static class ThaiAddressDataset
{
    /// <summary>Pinned upstream revision.</summary>
    public const string Version = "thailand-geography-json:b8b3fb91c7df1129ff5b43cb46f7fcffadd2156b";
    /// <summary>Original upstream file SHA-256.</summary>
    public const string Sha256 = "04169F87D7B7A21B0A5E14F05B7FE16BAB22E7BD9EE5340A7517C6170156F113";
    /// <summary>Returns unavailable rather than inventing fallback data on invalid/missing snapshots.</summary>
    public static ThaiAddressLookup Load(string root)
    {
        try
        {
            var path = Path.Combine(root, "Data", "ThaiAddresses", "geography.json");
            if (new FileInfo(path).Length > 8 * 1024 * 1024)
                return new("", []);
            var bytes = File.ReadAllBytes(path);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != Sha256)
                return new("", []);
            using var document = JsonDocument.Parse(bytes);
            string Code(JsonElement row, string name) => row.GetProperty(name).GetInt32().ToString(CultureInfo.InvariantCulture);
            string Name(JsonElement row, string name) => row.GetProperty(name).GetString() ?? "";
            var rows = document.RootElement.EnumerateArray().Select(row => new AddressCombination(new(Code(row, "provinceCode"), Name(row, "provinceNameTh"), Name(row, "provinceNameEn")), new(Code(row, "districtCode"), Name(row, "districtNameTh"), Name(row, "districtNameEn"), Code(row, "provinceCode")), new(Code(row, "subdistrictCode"), Name(row, "subdistrictNameTh"), Name(row, "subdistrictNameEn"), Code(row, "districtCode")), Code(row, "postalCode"))).ToArray();
            return new(Version, rows);
        }
        catch (IOException)
        {
            return new("", []);
        }
        catch (UnauthorizedAccessException)
        {
            return new("", []);
        }
        catch (JsonException)
        {
            return new("", []);
        }
        catch (InvalidOperationException)
        {
            return new("", []);
        }
        catch (KeyNotFoundException)
        {
            return new("", []);
        }
    }
}
