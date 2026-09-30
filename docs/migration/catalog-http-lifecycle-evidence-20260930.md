# Catalog HTTP lifecycle evidence — 2026-09-30

Tracking: [Catalog #27](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CatalogService/issues/27).
Base `fcf02d30453ff1402edd59228729aba4cd405b7e`; required main CI `36433603183` succeeded before edits.

## Reproduced defect and bounded repair

Six valid POSTs with independently generated RS256 signatures and each exact create permission
returned 400 rather than 201 through actual Production Program/MVC/application/repository/PostgreSQL.
A temporary diagnostic using the same production MVC validator classified its InvalidOperationException
as positional-record property validation metadata rejection; the diagnostic was removed after proof.
Removing only `property:` targets in CatalogModels.cs made all six actual POSTs return 201.
The existing Required/MaxLength rules, nullability, field types, request/response wire, routes,
permissions and business logic are unchanged. No ISO regex, Range, ETag or uniqueness rule was added.

Direct DataAnnotations Validator consumers were searched in Catalog and none consume these records.
Application callers currently receive records without separate object validation. The inspected
Intranet client/input DTOs are independent types with their own validation; they do not reference
the producer Application assembly. Moving producer annotation targets does not change their behavior.
The service already writes Unspecified UTC wall-clock timestamps; no CountryService timestamp
converter, migration, global provider switch or clock adjustment was ported.

## Actual acceptance boundary

One class-shared disposable PostgreSQL 18.1 container contains three independent databases.
Catalog's real migrations create its schema; current independent Country/Currency contexts create
their own disposable schemas, matching existing repository ownership. Each case resets only these
databases and the real cache adapter/backend. Actual Production Program DI, RS256 authentication,
permission handler, middleware, controllers, application service and repository are retained.
No custom auth handler, controller/service/repository substitution, SQL Server runtime or persistent
data/schema is used. External exchange-rate calls are not invoked.

The 46-case focused suite proves:

- `/Countries`, `/Currencies`, `/materials/MaterialGroups`, `/materials/Colors`,
  `/materials/SurfaceFinishes`, `/Materials`: create201/Location, detail200, update204,
  delete204, missing404, exact action grants and persisted/cache-refreshed values.
- Missing identity401, wrong/cross-action/employee wildcard grants403 and no persisted mutation.
  Malformed, expired, wrong issuer/audience/signature/algorithm tokens fail401 using normal
  Production RS256 validation, not the shared Testing authentication shortcut.
- Required null/empty/blank fields and each existing maximum length fail400 for POST and PUT.
  Exact maximums, lowercase country ISO values and nullable optional fields remain accepted.
  Posted ID/date values are ignored; server timestamps persist at PostgreSQL microsecond precision
  and provider reads remain Unspecified. Invalid writes do not invalidate a primed cache.
- Current PascalCase response, anonymous country/currency lists, sorted countries, separate
  lookup databases and no invented rich Country fields.
- All 13 material decimal fields persist exact two-decimal values; designation/reference fields,
  optional currency ID, real material-group FK, paginated/search/group projection, machinable and
  printable routes preserve their current behavior. No cross-database Currency FK is claimed.
- Color/finish association create201, duplicate204, detail/list, delete204/missing404 and relink;
  supplier zero400/missing material404/create201/read; material deletion removes all three link
  types while preserving independently owned lookup rows.
- Actual FK restrictions reject missing-group creation and deletion of linked group/color with
  the current generic500 response and no persisted mutation. This characterizes existing behavior,
  not an invented409/422 contract or a new runtime error-mapping policy.

## Individually retained source-owner evidence

Original committed objects were inspected read-only; no configuration/credential content was copied.

- `5fac706a7983a6d359b39acbd670e6800afe020e`: Country/Currency and material/color/group/finish
  controller/entity paths. Their bounded basic fields, server date ownership, CRUD/linked responses,
  anonymous lookup lists and material query/lifecycle behavior inform the acceptance above.
  This does not retire initial exchange-rate, operations, deployment, secrets or every other owner path.
- `72eb9f1949176392141951d35e6e06f7c30af4c2`: Country/Currency/Material model and associated
  startup ownership. Real field/date/decimal/link projections are proven in the new producer;
  historical startup/infrastructure/resource changes are not declared fully migrated by these tests.

Every other pending source behavior and earlier provenance remains unchanged. No Workflows ledger edit
or whole-source-owner closure is part of this PR; root retains ledger integration and protected merge.

## Executed validation and coverage

Exact CI pins: ServiceDefaults `003b255f0fb0f0bce032f5b5ff15d28be0c8c391`,
CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`.
Isolated dependency root: `B:/maliev-legacy/.validation-catalog-http-20260930`.
Set `DOTNET_PROCESSOR_COUNT=1`, `UseLocalMalievDependencies=true`, `MalievWorkspaceRoot` to that root.

Release `dotnet build Legacy.Maliev.CatalogService.slnx -c Release -warnaserror`: zero warnings/errors.
Focused `dotnet test Legacy.Maliev.CatalogService.Tests -c Release --no-build --no-restore
--filter FullyQualifiedName~CatalogHttpLifecycleTests`: 46/46 passed, zero skips.
Full same project with `--collect:"XPlat Code Coverage"`: 114/114 passed, zero skips.
TRX/Cobertura artifacts are outside Git under `B:/maliev-legacy/.artifacts/catalog-http-lifecycle-20260930`.

Unexcluded line/branch rates: API36.27%/27.18%, Application95.92%/78.18%, Data98.72%/88.23%,
Domain94.44%/100%. The six scoped controllers are100% lines/branches; Program94.59% lines.
ExchangeRates remains unexecuted in this slice; generated OpenAPI XML infrastructure remains in
the API denominator. Combined own-assembly line entries3217/3538=90.93%, including existing
generated migrations. This is not a claim that every assembly/branch reaches80%, nor that the
entire Catalog migration is complete. No exclusion, skip or generated-helper mirroring was added.

Production-derived data parity, cross-service auth, real Redis outage/recovery, exchange-rate external
traffic, full Aspire, TLS/deployment and operational cutover are deliberately not claimed.
No source/shared dependency/Workflows edits, deployment, persistent data/schema writes or cost occurred.
