# Legacy.Maliev.CatalogService

This repository is the public, sanitized legacy compatibility service for country,
currency, exchange-rate, material, color, material-group, and surface-finish data
that lived in three services under `R:\maliev-web`.

## Non-negotiable boundaries

- Keep the original `maliev-web` repository private.
- Do not copy monorepo Git history or legacy configuration files.
- Do not commit connection strings, service-account material, JWT keys, SMTP
  credentials, or generated secret-audit evidence.
- Preserve the legacy `/Countries`, `/Currencies`, `/currencies/exchangerates`,
  `/Materials`, `/materials/Colors`, `/materials/MaterialGroups`, and
  `/materials/SurfaceFinishes` route families until consumers are migrated.
- The database source of truth remains legacy until the PostgreSQL parity and
  cutover gates in `maliev-web` pass.

## Validation

Run from this repository root:

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format Legacy.Maliev.CatalogService.slnx --verify-no-changes --no-restore
dotnet list package --vulnerable --include-transitive
gitleaks git . --redact=100 --exit-code 0 --no-banner --no-color
```

## Service conventions

- Runtime: .NET 10.
- OpenAPI UI: Scalar through `Maliev.Aspire.ServiceDefaults`; no Swashbuckle.
- Logging: built-in `ILogger<T>` only; do not reintroduce `Maliev.LoggerService`.
- Cache: Redis keys must use the `legacy:catalog:` prefix.
- Auth: every protected endpoint uses a granular `legacy-catalog.*` permission;
  only the legacy country and currency list endpoints remain anonymous.
- Data model: preserve the legacy `Country`, `Currency`, `Material`, `Color`,
  `MaterialGroup`, and association table/column names; do not alter source SQL Server.
