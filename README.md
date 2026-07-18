# Legacy.Maliev.CatalogService

[![PR validation](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CatalogService/actions/workflows/pr-validation.yml/badge.svg)](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CatalogService/actions/workflows/pr-validation.yml)
[![Main CI](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CatalogService/actions/workflows/ci-main.yml/badge.svg)](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CatalogService/actions/workflows/ci-main.yml)

Temporary .NET 10 compatibility service extracted from the legacy Country,
Currency, and Material services in `maliev-web`. It consolidates their integer-key
schemas and PascalCase JSON contracts while the new `Maliev.CatalogService` is
developed independently.

## Architecture

The service uses clean dependency direction: `Api` calls `Application`, domain rules live in
`Domain`, and PostgreSQL/Redis adapters live in `Data`. It depends only on the public
`Legacy.Maliev.ServiceDefaults` and `Legacy.Maliev.CompatibilityContracts` source repositories
during CI and image builds, so the legacy runtime no longer consumes new-platform shared-library
source or private package credentials.

The unversioned routes are intentional compatibility exceptions. New MALIEV services use
versioned domain prefixes, but this temporary service keeps the exact paths consumed by the
legacy website until those consumers are migrated.

## API endpoints

| Purpose | Method | Route | Access |
| --- | --- | --- | --- |
| Countries | CRUD | `/Countries[/{id}]` | List anonymous; other actions protected |
| Currencies | CRUD | `/Currencies[/{id}]` | List anonymous; other actions protected |
| Live exchange rate | `GET` | `/currencies/exchangerates` | `legacy-catalog.currencies.read` |
| Materials | CRUD/list | `/Materials[/{id}]` | `legacy-catalog.materials.*` |
| Material flags | `GET` | `/Materials/machinable`, `/Materials/printable` | `legacy-catalog.materials.read` |
| Supplier links | `POST`, `GET` | `/Materials/{id}/suppliers[/{supplierId}]` | `legacy-catalog.materials.*` |
| Colors | CRUD | `/materials/Colors[/{id}]` | `legacy-catalog.colors.*` |
| Material-color links | CRUD | `/materials/{materialId}/colors[/{colorId}]` | `legacy-catalog.materials.*` |
| Material groups | CRUD | `/materials/MaterialGroups[/{id}]` | `legacy-catalog.material-groups.*` |
| Surface finishes | CRUD | `/materials/SurfaceFinishes[/{id}]` | `legacy-catalog.surface-finishes.*` |
| Material-finish links | CRUD | `/materials/{materialId}/surfacefinishes[/{surfaceFinishId}]` | `legacy-catalog.materials.*` |
| Scalar UI | `GET` | `/catalog/scalar` | Anonymous |

## Runtime boundaries

- Legacy routes: `/Countries`, `/Currencies`, `/Materials`, and `/materials/*`
- Scalar: `/catalog/scalar`
- PostgreSQL database: `Catalog` on a `legacy-postgres-*` CloudNativePG instance
- Redis key prefix: `legacy:catalog:`
- Country and currency lists remain anonymous; all other endpoints require granular
  `legacy-catalog.*` permissions.

This service does not modify the SQL Server source. PostgreSQL promotion requires
the artifact-backed parity and cutover gates tracked in `MALIEV-Co-Ltd/maliev-web`.

Deployment is intentionally validation-only until a dedicated
`legacy-maliev-catalog` Workload Identity Federation provider and
`maliev-gitops/3-apps/_legacy-catalog-service` manifest path exist. The existing
`maliev-catalog-service` GitOps path belongs to the new implementation and must
not be overwritten by this legacy compatibility service.

## Validate

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format Legacy.Maliev.CatalogService.slnx --verify-no-changes --no-restore
dotnet list package --vulnerable --include-transitive
```
