# Standard JSON scalar values for legacy names

The original country, currency and four material-resource controllers at
5fac706a7983a6d359b39acbd670e6800afe020e bind plain string entity properties and
copy names directly. Their Newtonsoft configuration has no custom string
converter. The pinned Newtonsoft 13.0.3 string contract selects ReadAsString:
JsonTextReader.ReadStringValue maps native true/false to lowercase text, and
ParseNumber(ReadAsString) retains the raw number lexeme after invariant floating
point validation. String input stays literal; null remains null on fresh models.
This is source-derived evidence, not original deployed binary/HTTP execution.

The request-property converter covers only Country.Name, Currency.ShortName and
LongName, MaterialGroup.Name, Color.Name, SurfaceFinish.Name and Material.Name.
It permits standard JSON numbers and booleans while preserving output strings.
Negative zero, exponent notation and overflow/underflow lexemes remain text,
without numeric normalization. It retains existing string, null/omitted-name,
maximum-length, route, authorization and other-property behavior. Response DTOs,
global JSON settings, persistence and permissions are unchanged. A named OpenAPI
schema transformer selects only properties using this converter and retains
their string type for generated clients. The existing literal AddOpenApi call
and generated XML documentation registration remain unchanged.

Proposed new HTTP/PostgreSQL executions: 147 (seven fields, ten standard scalar
literals, POST/PUT = 140; seven per-field null/omitted/oversized/array/object and
401/403 controls). Tests check created Location/IDs, detail/list/persistence,
CreatedDate, other supplied fields, six parent sentinels, all association rows
and nine table counts. One additional served OpenAPI regression verifies all
seven request names on POST/PUT remain required string fields without database
work. Total new forecast 148; full forecast 694. Updates include links to target material/color/finish
rows so relationship preservation is observed. All 546 prior execution names,
IDs and multiplicities remain. Shared fixture body, original
20 diagnostics, immutable raw exports/receipt and owned 80 percent coverage
including generated code with zero exclusions are unchanged.

Hex/octal/NaN/Infinity nonstandard lexical forms, coercion of other string
properties, original null/omitted/length failure statuses and envelopes, original
runtime, consumer-wide serializer parity and whole-source closure remain OPEN.
No global formatter or source SQL change is proposed.

The schema correction follows the pinned .NET 10
[schema exporter](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Text.Json/src/System/Text/Json/Schema/JsonSchemaExporter.cs),
which produces an unconstrained schema for user converters, and ASP.NET's
[primitive schema mapping](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/OpenApi/src/Extensions/JsonNodeSchemaExtensions.cs),
which applies formats rather than restoring a missing primitive type.

Local Release build, focused/full tests, formatting/static/security/package
audit and HTTP/PostgreSQL checks NOT RUN: no qualified Commerce SDK custody
manager/allocation. The owner-approved migration validation-order exception
permits reviewed draft publication before local validation; it waives no check.
This integrated source-review draft is not published, validated or accepted.
Exact-head and protected-main hosted checks and original raw readback remain
required. No deployment, cutover or complete-migration claim.
