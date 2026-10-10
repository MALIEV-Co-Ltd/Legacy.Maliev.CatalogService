# Literal material resource names

The original material, material-group, color and surface-finish controllers at
`5fac706a7983a6d359b39acbd670e6800afe020e` bind their entity types and copy `Name`
directly for POST and PUT. Their model properties have no validation attributes,
initializers or nullable annotations. Their projects do not enable nullable
reference types. EF mappings require non-null names with maximum length 50;
the controllers do not trim strings or reject empty or whitespace-only names.
The original JSON configuration uses Newtonsoft JSON with null values ignored.
Newtonsoft's empty-string coercion explicitly excludes `string` properties.
These are source-derived conclusions; the deployed original binary and HTTP
execution have not been observed.

Catalog now permits empty and whitespace-only names in these four requests by
setting `Required.AllowEmptyStrings`. It preserves the literal string in create
responses, detail/list responses and persistence. Other fields, IDs, timestamps,
parent links, routes, granular permissions and maximum lengths are unchanged.
Null and omitted names retain Catalog's existing 400 response. That status is a
target contract control, not proven original failure-envelope parity. Original
null/missing/oversized-name outcomes remain separate open obligations.

The new HTTP/PostgreSQL test class contains 34 executions: 24 literal cases
(four resources, three literals, POST/PUT), four null/length/auth controls,
two material missing-parent rollback controls, and four omitted-name controls.
The existing six required-field executions retain their names and multiplicities:
country/currency still reject empty and blank names; material resources keep
their null rejection. The existing invalid-update cache/read test now uses a
51-character color name because a blank name is accepted by this source repair;
its rejection, persistence and authoritative-read assertions remain intact.
The shared fixture body remains unchanged. Full execution
forecast increases from 491 to 525; original 20 diagnostics, immutable raw
exports/receipt, owned 80% coverage floor and zero-exclusion policy remain intact.

Local Release build, focused tests, full suite, format/static/security/package
checks and HTTP/PostgreSQL execution: NOT RUN. Commerce has no qualified local
SDK custody manager or allocation. The owner-approved migration exception permits
a reviewed draft to reach hosted validation before local execution; it waives no
check. Exact proposed-head and protected-main hosted checks must pass before
this slice is accepted. No deployment, database cutover or whole-source closure.
