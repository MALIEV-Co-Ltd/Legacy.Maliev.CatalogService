# Catalog dormant publisher validation adoption — 2026-10-01

Frozen candidate for independent review under bounded [Catalog #33](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CatalogService/issues/33). No commit or publication performed.

## Scope and interface

Canonical main/live origin was clean at `b6dc3285edd1ed5517012285417cc4a56df07879`, with no open PRs. This exclusive workspace/branch is `B:/maliev-legacy/.worktrees/catalog-publisher-validation-20261001` / `codex/catalog-publisher-validation-20261001`. Other Catalog workspaces remain untouched.

Only the publisher pin changes from `6e3bb55f5ff3ee2b69dd6b4aee6333777ba0ed36` to accepted Workflows `503e8846390a597c267d2889b33a9c26863389b3` (root verified exact-main CI `36823157482` successful), together with job-local `actions: read`. Committed producer diff from the actual old `6e3bb55` shows no change to the reusable workflow's input definitions or digest output. New read permission allows the producer to inspect trusted exact-SHA main validation before OIDC/registry actions. It is not an activation grant.

Workflow-level permissions remain only `contents: read`; publisher permissions are exactly `contents: read`, `actions: read`, `id-token: write`. The complementary planned-only gate and `LEGACY_DEPLOY_ENABLED == 'true'` publication gate remain unchanged; nothing enables the variable. All eight image/Dockerfile/context/Defaults/Contracts/environment/WIF/account inputs, event/ref handling and concurrency remain unchanged. Private CI action/dependency refs, Dockerfile, runtime, reconciliation defaults and database behavior are untouched.

No provider requests, WIF/registry activation, GitOps, deployment, schema/data changes, global retirement or Aspire acceptance are claimed. Repeated producer observations narrow but cannot atomically eliminate a GitHub run changing after observation. Parent acceptance, real publication/digest consumption and deployment remain separate gates.

## Source cohort — individual partial records

Read-only committed mirror `B:/maliev-legacy/.artifacts/source-commit-mirror-20260930.git`, checkpoint `bed10c7d15e0698e0b75f1329d0f312937f5d77f`. Catalog consolidates these source service owners; no record is blanket-closed.

| Full source SHA | Source owner paths / behavior | Bounded disposition |
| --- | --- | --- |
| `00ec830615c15b5e4e227046712247b11df0100f` | `Maliev.CountryService.Api/deploy.ps1`, `Maliev.CurrencyService.Api/deploy.ps1`, `Maliev.MaterialService.Api/deploy.ps1`: external step failure checks and cleanup | Exact validation prerequisite strengthens fail-closed delivery, but historical gcloud/kubectl script execution and full service-owner parity remain unproven. |
| `72163e9ae11f39f6579423841a2e20529b986fab` | Each of those three API directories' `deploy.ps1` and `deploy-service.ps1`: reliable status propagation | Immutable shared workflow failure propagation is consumed; historical wrappers/deployment are not executed or accepted by this slice. |
| `f8921b1b1d5846eeaff999af10b640011655d1d4` | Those three `deploy.ps1` paths: throwaway manifest rendering, template unchanged, finally cleanup | Immutable-image caller does not render or mutate source deployment manifests. Adoption preserves that separation; no historical deployment-template parity claim. |

No source/ledger writes, source-history copying or whole-owner closure.

## Test-first and validation

Fresh `--no-hardlinks` private clean detached clones at `TestResults/.private` match actual Catalog CI (not another service's pins): Defaults `003b255f0fb0f0bce032f5b5ff15d28be0c8c391`, Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. All service/dependency outputs are isolated Release outputs inside this workspace.

The existing parsed `PublicationDependencyTests` is strengthened, not replaced with substring checks: exact full pin, top contents-only permissions, exact three publisher permissions, complementary dormant gate, job count and all eight input values. Every existing Dockerfile/dependency assertion remains. The only superseded assertion is the obsolete producer pin; all other test files are unchanged.

| Phase | Actual outcome | Retained evidence |
| --- | --- | --- |
| Unchanged Release baseline | 0 warnings/errors; 163 passed, zero failed/skipped | `TestResults/publisher-baseline/baseline.trx` |
| Strengthened contract, unchanged YAML | 1 genuine parsed-pin assertion failure; OIDC control passed, zero skips | `TestResults/publisher-red/red.trx` |
| Final Release build | 0 warnings/errors | Terminal output |
| Focused publisher + existing CI contracts | 16 passed, zero failed/skipped | `TestResults/publisher-focus/focus.trx` |
| Final unfiltered suite | 163 passed, zero failed/skipped | `TestResults/publisher-full/full.trx` |

Commands from the owned workspace:

```powershell
dotnet build Legacy.Maliev.CatalogService.Tests/Legacy.Maliev.CatalogService.Tests.csproj -c Release -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/catalog-publisher-validation-20261001/TestResults/.private
dotnet test Legacy.Maliev.CatalogService.Tests/Legacy.Maliev.CatalogService.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~PublicationDependencyTests|FullyQualifiedName~PublishWorkflowPermissionContractTests|FullyQualifiedName~WorkflowContractTests' --logger 'trx;LogFileName=focus.trx' --results-directory TestResults/publisher-focus
dotnet test Legacy.Maliev.CatalogService.Tests/Legacy.Maliev.CatalogService.Tests.csproj -c Release --no-build --logger 'trx;LogFileName=full.trx' --results-directory TestResults/publisher-full
$env:UseLocalMalievDependencies='true'
$env:MalievWorkspaceRoot='B:/maliev-legacy/.worktrees/catalog-publisher-validation-20261001/TestResults/.private'
dotnet format Legacy.Maliev.CatalogService.slnx --verify-no-changes --no-restore
& C:/Users/natth/go/bin/actionlint.exe
dotnet list Legacy.Maliev.CatalogService.slnx package --vulnerable --include-transitive
```

Whole format/actionlint passed with no diagnostics; all five service projects reported no vulnerable packages. Accepted Workflows signing-resource scanner passed; current-tree credential scanner reported zero findings. Redacted Gitleaks workflow/test/documentation scopes and all 32 reachable history commits passed. Final documentation readback and diff whitespace checks passed; baseline/final TRX name comparison found zero missing original tests. No coverage exclusions, waived API threshold or runtime coverage claim is introduced.

Five audit dispositions: Actions is covered by parsed actual caller/CI contracts and actionlint; unchanged CI-main/develop/staging files remain present. Explicit dormant legacy scope supersedes the Actions skill's modern deployment-scaffolding suggestion. API/messaging/model/migration/runtime performance boundaries are unchanged; no fabricated extra API, message, DDL or benchmark acceptance.

Root owns tracking/integration and independent acceptance. Intended file list: `.github/workflows/publish-image.yml`, `Legacy.Maliev.CatalogService.Tests/Workflows/PublicationDependencyTests.cs`, this document. No commits, pushes, GitHub/provider/deployment/persistent-data writes occurred.

## Independent root acceptance

Root independently rebuilt the direct test project with the same isolated private graph in Release: zero warnings and zero errors, including private dependency outputs. Focused 16 and unfiltered 163 tests passed with zero skips. Whole-solution formatting, actionlint, all five transitive vulnerability audits, scoped redacted secret scanning and whitespace checks passed. This acceptance concerns only the frozen three-file caller contract; source-owner, provider, runtime and Aspire gates are not closed by it. Protected-head and post-merge main CI remain required before marking the bounded issue done.
