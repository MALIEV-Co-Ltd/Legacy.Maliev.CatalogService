using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.CatalogService.Application.Lookups;

/// <summary>An administrative entity with a stable code and bilingual names.</summary>
/// <param name = "Code">Administrative code.</param>
/// <param name = "NameTh">Thai name.</param>
/// <param name = "NameEn">English name.</param>
/// <param name = "ParentCode">Parent code, absent for provinces.</param>
public sealed record AdministrativeArea([property: JsonPropertyName("code")] string Code, [property: JsonPropertyName("nameTh")] string NameTh, [property: JsonPropertyName("nameEn")] string NameEn, [property: JsonIgnore] string? ParentCode = null)
{
    /// <summary>Province relationship on district projections.</summary>
    [JsonPropertyName("provinceCode")]
    public string? ProvinceCode => ParentCode?.Length == 2 ? ParentCode : null;

    /// <summary>District relationship on subdistrict projections.</summary>
    [JsonPropertyName("districtCode")]
    public string? DistrictCode => ParentCode?.Length == 4 ? ParentCode : null;
}

/// <summary>One valid administrative and postcode combination.</summary>
/// <param name = "Province">Province.</param>
/// <param name = "District">District.</param>
/// <param name = "Subdistrict">Subdistrict.</param>
/// <param name = "Postcode">Postal code.</param>
public sealed record AddressCombination([property: JsonPropertyName("province")] AdministrativeArea Province, [property: JsonPropertyName("district")] AdministrativeArea District, [property: JsonPropertyName("subdistrict")] AdministrativeArea Subdistrict, [property: JsonPropertyName("postcode")] string Postcode);
/// <summary>A dataset-bound page; empty items means no match.</summary>
/// <param name = "DatasetVersion">Immutable version.</param>
/// <param name = "Items">Page items.</param>
/// <param name = "HasMore">Further matching items exist.</param>
/// <param name = "NextCursor">Opaque continuation, bound to query and version.</param>
public sealed record LookupPage<T>([property: JsonPropertyName("datasetVersion")] string DatasetVersion, [property: JsonPropertyName("items")] IReadOnlyList<T> Items, [property: JsonPropertyName("hasMore")] bool HasMore, [property: JsonPropertyName("nextCursor")] string? NextCursor);
/// <summary>Server-enforced AND filters.</summary>
public sealed class AddressQuery
{
    /// <summary>Optional name query.</summary>
    [StringLength(128), JsonPropertyName("q")]
    public string? Q { get; set; }

    /// <summary>Province code.</summary>
    [RegularExpression("^[0-9]{2}$"), JsonPropertyName("provinceCode")]
    public string? ProvinceCode { get; set; }

    /// <summary>District code.</summary>
    [RegularExpression("^[0-9]{4}$"), JsonPropertyName("districtCode")]
    public string? DistrictCode { get; set; }

    /// <summary>Subdistrict code.</summary>
    [RegularExpression("^[0-9]{6}$"), JsonPropertyName("subdistrictCode")]
    public string? SubdistrictCode { get; set; }

    /// <summary>ASCII or Thai postal digits.</summary>
    [StringLength(5, MinimumLength = 5), JsonPropertyName("postcode")]
    public string? Postcode { get; set; }

    /// <summary>Maximum page size.</summary>
    [Range(1, 50), JsonPropertyName("limit")]
    public int Limit { get; set; } = 20;

    /// <summary>Opaque query-bound continuation.</summary>
    [StringLength(512), JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

/// <summary>A bounded pasted-address request.</summary>
public sealed class ResolveAddressRequest
{
    /// <summary>Original customer text; never log it.</summary>
    [Required, StringLength(2048, MinimumLength = 1), JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>Optional entered-field constraints.</summary>
    [JsonPropertyName("constraints")]
    public AddressQuery? Constraints { get; set; }
}

/// <summary>Confidently extracted original-text span, UTF-16 offsets.</summary>
/// <param name = "Start">Offset.</param>
/// <param name = "Length">Length.</param>
/// <param name = "Kind">Administrative level or postcode.</param>
/// <param name = "Text">Exact original substring.</param>
public sealed record AddressSpan([property: JsonPropertyName("start")] int Start, [property: JsonPropertyName("length")] int Length, [property: JsonPropertyName("kind")] string Kind, [property: JsonPropertyName("text")] string Text);
/// <summary>Fields shared by the complete match set, never just a preview.</summary>
/// <param name = "Province">Unique province.</param>
/// <param name = "District">Unique district.</param>
/// <param name = "Subdistrict">Unique subdistrict.</param>
/// <param name = "Postcode">Unique postcode.</param>
public sealed record UniqueAddressFields([property: JsonPropertyName("province")] AdministrativeArea? Province, [property: JsonPropertyName("district")] AdministrativeArea? District, [property: JsonPropertyName("subdistrict")] AdministrativeArea? Subdistrict, [property: JsonPropertyName("postcode")] string? Postcode);
/// <summary>Resolution is administrative extraction, not deliverability validation.</summary>
/// <param name = "DatasetVersion">Dataset version.</param>
/// <param name = "OriginalText">Unchanged input.</param>
/// <param name = "NormalizedText">Thai digits normalized, same UTF-16 length.</param>
/// <param name = "Outcome">exact, ambiguous, not-found or conflict.</param>
/// <param name = "Candidates">Bounded preview.</param>
/// <param name = "HasMore">More than 50 candidates.</param>
/// <param name = "UniqueFields">Complete-set uniqueness.</param>
/// <param name = "DetailText">Input with confident administrative spans removed.</param>
/// <param name = "ExtractedSpans">Original-text offsets.</param>
/// <param name = "Conflicts">Conflicting field names.</param>
public sealed record AddressResolution([property: JsonPropertyName("datasetVersion")] string DatasetVersion, [property: JsonPropertyName("originalText")] string OriginalText, [property: JsonPropertyName("normalizedText")] string NormalizedText, [property: JsonPropertyName("outcome")] string Outcome, [property: JsonPropertyName("candidates")] IReadOnlyList<AddressCombination> Candidates, [property: JsonPropertyName("hasMore")] bool HasMore, [property: JsonPropertyName("uniqueFields")] UniqueAddressFields UniqueFields, [property: JsonPropertyName("detailText")] string DetailText, [property: JsonPropertyName("extractedSpans")] IReadOnlyList<AddressSpan> ExtractedSpans, [property: JsonPropertyName("conflicts")] IReadOnlyList<string> Conflicts);
/// <summary>Truthful suggestion fields; unknown details remain null.</summary>
/// <param name = "NameTh">Available Thai name.</param>
/// <param name = "NameEn">Available English name.</param>
/// <param name = "TaxId">Provider identifier.</param>
/// <param name = "RetrievedAt">Fetch time.</param>
public sealed record CompanySuggestion([property: JsonPropertyName("nameTh"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? NameTh, [property: JsonPropertyName("nameEn"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? NameEn, [property: JsonPropertyName("taxId"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? TaxId, [property: JsonPropertyName("retrievedAt")] DateTimeOffset RetrievedAt)
{
    /// <summary>Not verified by suggestion access.</summary>
    [JsonPropertyName("status"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Status => null;

    /// <summary>Not verified by suggestion access.</summary>
    [JsonPropertyName("companyType"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? CompanyType => null;

    /// <summary>Not verified by suggestion access.</summary>
    [JsonPropertyName("objectives"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Objectives => null;

    /// <summary>Not verified by suggestion access.</summary>
    [JsonPropertyName("registeredAddress"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? RegisteredAddress => null;
}

/// <summary>Company capability and availability are explicit.</summary>
/// <param name = "Outcome">matches, no-match, rate-limited, unavailable or unsupported.</param>
/// <param name = "Items">Verified available suggestion fields.</param>
public sealed record CompanyLookup([property: JsonPropertyName("outcome")] string Outcome, [property: JsonPropertyName("items")] IReadOnlyList<CompanySuggestion> Items)
{
    /// <summary>Minimum client retry delay for an explicit throttled result.</summary>
    [JsonPropertyName("retryAfterSeconds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RetryAfterSeconds { get; init; }

    /// <summary>Provider provenance.</summary>
    [JsonPropertyName("provider")]
    public string Provider => "creden";

    /// <summary>This adapter never claims detail capability.</summary>
    [JsonPropertyName("capability")]
    public string Capability => "suggestion";

    /// <summary>Suggestion provider does not support paging.</summary>
    [JsonPropertyName("hasMore")]
    public bool HasMore { get; init; }
}

/// <summary>Isolated company provider boundary.</summary>
public interface ICompanyLookup
{
    /// <summary>Searches with bounded input; respects caller cancellation.</summary>
    Task<CompanyLookup> SearchAsync(string query, string queryType, string language, int limit, CancellationToken cancellationToken);
}
