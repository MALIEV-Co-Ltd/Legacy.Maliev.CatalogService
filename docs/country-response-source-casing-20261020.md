# Country response casing source proposal

Status: unreferenced source proposal only. No build, test, financial operation, persistent data change, deployment, contract pin or workflow was executed or changed.

## Actual missing behavior

Catalog protected main `f3bf2c26f23617ceb4efc45d12a199e375d7be4e`, tree `acc4c7e62ab879138e0c6f5db4aebf3a6d2e11dc`, serializes CountryResponse with PascalCase.
The retained Country MVC producer uses the eight source names:
`id`, `name`, `continent`, `countryCode`, `iso2`, `iso3`, `createdDate`, `modifiedDate`.

This proposal assigns JsonPropertyName only to CountryResponse. It preserves the .NET property names/types, request binding, all values and DateTime handling, routes, Location, anonymous list behavior, permission checks, repositories, database roles and migrations.
The six nullable response properties remain nullable and omitted by normal Program's WhenWritingNull; their Country-only schema metadata becomes optional. Id and Name keep their existing required metadata.
Other DTOs and the global MVC/HTTP JSON options remain byte unchanged. The held FX casing proposal is not activated.

## Retained source and full-commit qualification

| Source witness | Full source object |
| --- | --- |
| Final retained source | 770cb58032969ae4a0fe169686b53ab61a44fff8 |
| Original Country controller | 2ebceeee7ed09231019758d69843f5ebcc77f7bc |
| Final Country Startup | f4103ed4fa906b159e2b895a8f28f2e1785d2322 |
| Final Country project | e286cd03082238c08dbfe848f0a85a2d1c47036a |
| Original eight-property Country model | 9e4229092328bad7ce56bb9d446e0b178326648d |
| Model predecessor at parent of source update | 467f3755753ecac98d7cfdb8dc9d69e54f73cc9f |

The original controller returns List<Country>/Country directly. Startup calls AddNewtonsoftJson with NullValueHandling.Ignore and ReferenceLoopHandling.Ignore, without a naming override. The Country project pins net8.0/MVC.NewtonsoftJson8.0.28.
The official default provider at exact tag v8.0.28, blob `2f1cfd6f20c6f9c17219f537e613c07d36af3ac1`, installs CamelCaseNamingStrategy. The v8.0.3 witness previously inspected for initial source has the identical default-provider blob:
https://github.com/dotnet/aspnetcore/blob/v8.0.28/src/Mvc/Mvc.NewtonsoftJson/src/JsonSerializerSettingsProvider.cs

The source update `72eb9f1949176392141951d35e6e06f7c30af4c2` has parent `023aa2f143fa5a8f557d72c1302b9add64792d6a`.
Complete, nontruncated recursive tree metadata was compared: tree `09d037960440d07eca308469ecc89e638dab08b9` versus `8582ec733ad107b321a0a21da2da3919dce583dc`, 2149 versus 2156 leaf files.
All 242 changed leaf paths were retained as metadata, covering 56 top-level prefixes. Country's changed paths are:
- Maliev.CountryService.Api/Maliev.CountryService.Api.csproj
- Maliev.CountryService.Api/Startup.cs
- Maliev.CountryService.Data/Database/CountryContext/Country.cs
- Maliev.CountryService.Data/Database/CountryContext/CountryContext.cs
- Maliev.CountryService.Data/Maliev.CountryService.Data.xml

Other owners' paths are retained in the separate qualification receipt, not treated as Catalog completion.
The controller's origin is initial `5fac706a7983a6d359b39acbd670e6800afe020e` (no parents); its mixed-owner scope is already established by the original Web page witness `dc1351d2dade4fde887f85512fbb2653b43c71fc`. No whole initial source inventory or configuration was fetched.
This is a specific response-contract correction, not whole-source closure or count credit.

## Actual producer and consumers

Catalog CountriesController blob `0b90ed4e3b538fb8dddad2153e87ea1119fe402b` returns CountryResponse for POST, protected detail GET and anonymous list GET.
CatalogApplicationService blob `9fa3f2c052669c55285341910dd838efc07d6785` projects Country entities directly; list/detail reads use repositories. No nested Material or FX response reuses CountryResponse.
The generic distributed cache blob `83f40e6a0eaaaa9657538638ab24a2aebf235512` already uses JsonSerializerDefaults.Web and retains its existing key/lifetime behavior.

Inspected consumers:
- Intranet LegacyCatalogClient `c117e260531dd236aebb9be2e826d800b00ee836` reads /Countries with ReadFromJsonAsync<T> without explicit options; registration uses Services:Catalog.
- Intranet employee BFF Program `e831feb7aa910b604debafe5330d57ef901b49fa` binds its anonymous Country client to Services:Catalog. Coordinator `be189165d4ed13f3edd02b02bbf9380e4861101b` reads EmployeeAdministrationCountry[] with the same no-options overload and retains positive-ID/nonblank-name/unique-ID checks.
- Web CountryClient `e8c8da42f5c5fad3a51026f5b86242bd99baa6eb` and CustomerMemberDetailClient `2c5df09cd7c13890f54183826a5a6d17f06c8bd4` use the same no-options reader. Web registration `13a0ac494982ef9419c149d53e82423ac13359c3` binds the countries name to endpoints.Country, not endpoints.Catalog; these are compatible reader patterns, not proof of a Web-to-Catalog deployment join.

Exact .NET10 source HttpContentJsonExtensions `e50715859e6fe3eca80ba633be95ab96143b011e` routes that overload through options:null to JsonSerializerOptions.Web.
JsonSerializerOptions source `00f913e068ee3883a2e369be639872894bfc502f` explicitly sets PropertyNameCaseInsensitive=true for Web defaults.
No consumer production edit, new credential, live request or financial execution is needed to author this nonfinancial proposal. Runtime consumer acceptance still requires the appropriate hosted checks; no live join is claimed.

## Regressions and coupled raw evidence

Existing native case names, data rows, permission/mutation checks, literal values and database snapshot selectors are retained.
Only Country HTTP field selectors change to exact camelCase. Shared tests retain other resources' PascalCase. Database snapshots continue to use PascalCase.
Two new pure Country serialization/reader rows compare the configured original MVC settings and default existing reader behavior; they cover all eight fields and nullable omission, and assert that Color response casing is unchanged.
One new served OpenAPI Fact verifies Country names/nullable optional metadata and unchanged Color schema casing.

The diagnostic raw reader is an actual payload consumer. New immutable-head manifest metadata `countryResponseCase:camelCase` chooses exactly eight camelCase HTTP keys.
The unchanged PascalCase database schema is selected separately. Validation checks the exact raw field allowlist and required keys before mapping to the original canonical comparison dictionary.
No general case folding or mixed-case fallback is added.
An absent field in an actual historical manifest selects the original strict PascalCase branch; explicit null, boolean, PascalCase or unknown metadata is rejected.
SOURCES, source-head/tree binding, receipt schema, original raw bytes/hashes, 48 export filenames, native inventory/all16 counters, literal comparisons and coverage floors remain unchanged.
Synthetic controls retain all old controls and add strict wrong-case/privacy/duplicate/type checks and explicit immutable historical metadata-head readback. They do not claim native producer execution or actual historical receipt replay.

Actual accepted #109 historical metadata is separately retained: manifest blob `ca45faed9a9721f40fee11f07ad734100e97fc02` has no countryResponseCase field; main `f3bf2c26f23617ceb4efc45d12a199e375d7be4e` receipt `dd4e22ceeb0acd5f553af87ab5c07d00710e743e` and original log seal `7efd07cb9fdf1906864516e4af016a7f79755714` remain immutable. No original log was re-fetched or rewritten for this work.

## Validation status

All new implementation/regressions/reader controls are NOT RUN. The source-only lane forbids local helpers and execution.
Authored fullForecast 4582 to 4585 represents two new literal theory rows plus one new OpenAPI Fact, not passed results or invented GUIDs.
Root must review the complete source/reader proposal before publication. Exact-head hosted Release build zero warnings/errors, focused Country/reader/schema checks, full suite, formatting, package/security audits, unchanged native guards, four unchanged 80% floors and fresh-main checks remain required.
No CI workflow, contract pin, data/DDL, IAM, deployment, original repository or held FX/financial lane was changed.
