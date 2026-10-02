# Catalog external configuration and build-context proof

Initial TEST/DESIGN scope, subsequently expanded only to the reviewed four-line ignore repair. Bounded tracking: [Catalog #35](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CatalogService/issues/35). Base protected main `92a18dc9e87fce9d31a352b9e62dad1c5cac71cd`, exact-main CI run `36831732774` succeeded. Existing #27 broader acceptance remains open; #15/#34 cover Docker dependency and dormant publisher prerequisites, not effective context exclusion.

## Individually retained source provenance

Immutable source objects only; no original configuration/credential content is copied.

| Full source SHA | Full parents | Owned source changes |
| --- | --- | --- |
| `2aab25eb07894fc0267b03b85bad96490219d2fa` | `56014f2efc7c7f24b777c51d17a454e023d12bdd` | MaterialContext external design-time connection; Data XML/resource generated companions |
| `7d6f46f53cbab853ca9c25e385af067cfff6238a` | `7a660e9630e2ff10dbdf6f03766f89f125fdba4f` | API appsettings externalization and Material scaffold environment input |
| `eb8ed86672bd9afccc6560b547b734d0fcd7363b` | `6da103d234ea312ce3b404fefaed343c172517b1`, `868b5909406cc3759254ee10520e04d4ad5beab0` | First-parent secret-remediation merge: above paths plus API ServiceExtensions/Startup/XML |
| `a649db99a27bda65274fe1b18866ae226d3c69cf` | `d5d67b65a69ae4b53bfe94e2a669a5d4680696ab`, `eb8ed86672bd9afccc6560b547b734d0fcd7363b` | Separate first-parent merge carrying those changes; not blanket merge retirement |
| `03eaff1194c3ae2a54ceefeae31deffaff90436f` | `5b6d94a02a7578783e0980af10ecd36856aba024` | Material API Dockerignore recursive private/build-context exclusions |

## Independent acceptance boundary

Only NEW Integration/CatalogBuildContextParityTests.cs and this design are owned initially. The actual CatalogDbContextFactory is called with absent/blank external configuration and an explicit synthetic PostgreSQL connection pointing to port 1. Provider/options/closed connection are inspected; no database is opened. Process-wide environment is restored in finally; the collection isolates environment cases from concurrent tests.

The real Docker CLI/BuildKit evaluates the unchanged repository root .dockerignore against a uniquely owned TestResults scratch directory. FROM scratch and COPY have no RUN, network or base-image pull. Local output contains the actual admitted COPY files. Independent synthetic .git/.env/.env.*/*.env paths must not reach COPY, while real files copied from exact privately pinned dependency checkouts and Catalog source must retain identical bytes. This is context proof, not an application-image, deployed secret, database, Auth/IAM grant or runtime parity claim.

Docker endpoint/context must be local before build; remote ambient configuration fails without starting a build. Output readers are bounded to 16 KiB each, withheld on failure, and the owned CLI process is terminated on budget/timeout failure. There is no retry/skip. Owned synthetic input/output is retained for root review; no unrelated container/image/cache deletion or pruning.

CI dependency pins: ServiceDefaults `003b255f0fb0f0bce032f5b5ff15d28be0c8c391`; CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. Private ignored checkouts are under this worktree's .dependencies, never shared outputs.

## Sequencing and exclusions

Baseline Release build succeeded with zero warnings/errors. Build the NEW tests before focused pure cases. Acquire the serial local Docker window before the one context case. Preserve actual RED separately from missing Docker prerequisites; any root .dockerignore repair needs root review first. No factory/runtime, old test, schema, workflow, provider, ledger, source-owner disposition, commit, push, GitHub or deployment changes are authorized.

Historical SQL scaffold execution, generated resource/XML reproduction, operational secret population, all source merge/global owners, live production data parity and broader Catalog #27 remain pending. Passing controls are existing behavior proof, not invented RED or financial/provider-policy authorization.

## Reached initial evidence (before any ignore repair)

Baseline and NEW-test Release builds: `dotnet build Legacy.Maliev.CatalogService.slnx -c Release -warnaserror -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=<owned worktree>/.dependencies` (subsequent builds used `--no-restore`): zero warnings/errors.

Focused pure command: `dotnet test Legacy.Maliev.CatalogService.Tests/Legacy.Maliev.CatalogService.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~CatalogBuildContextParityTests&FullyQualifiedName!~Actual_Docker_context"`: 12 PASS, zero failures/skips, `TestResults/catalog-context-pure-final/pure.trx`. Four actual factory cases prove existing fail-closed/Npgsql behavior; eight fixture endpoint guards do not prove product migration completion.

After root released the serial Docker window, fresh Release again built zero warnings/errors. Exact context filter `FullyQualifiedName~Actual_Docker_context_excludes_nested_private_sentinels` reached 1 genuine FAIL, zero skips, `TestResults/catalog-context-reached/context.trx`. Docker prerequisite and scratch build completed successfully. All six independent legitimate source byte assertions passed before the exclusion assertion.

Actual admitted synthetic paths: root `.env.synthetic`; ServiceDefaults dependency `.git/config`, `.env`, `.env.synthetic`, `runtime.env`; CompatibilityContracts dependency `.git/config`. Root `.git/config`, root `.env`, and bin/obj sentinels were excluded. Owned scratch input/output is retained under `TestResults/catalog-context/<unique identity>`; no secret or real Git metadata was copied into that context.

Minimal proposal ONLY, not yet authorized/applied: append recursive `**/.git`, `**/.env`, `**/.env.*`, `**/*.env` to the root .dockerignore, preserving legitimate dependency sources and every existing rule. Re-run actual context proof after root reviews tests/design; no factory/runtime change is indicated. No full suite is claimed while the intentional regression remains RED. One initial format invocation used an unsupported MSBuild property argument (CLI diagnostic, not product failure); corrected invocation sets the owned dependency property in process environment.

## Approved bounded repair and focused GREEN

After root read the complete NEW tests/design and reproduced-path report, only the four proposed recursive exclusions were appended. Every old rule, runtime/factory and old test remains unchanged. Fresh Release built zero warnings/errors; the entire NEW class reached 13 PASS, zero failures/skips (`TestResults/catalog-context-green/green.trx`). Actual scratch context now excludes all ten independent sentinels and retains all six actual Catalog/pinned-dependency source files byte-for-byte. The original reached RED and both scratch exports remain available.

Whole format passed using owned dependency environment. A first vulnerability-audit invocation omitted that environment and implicitly restored pre-existing sibling dependency assets; this is a preparation mistake, not valid isolated-graph evidence. No sibling build or cleanup/revert was performed; root was notified. The corrected whole-solution transitive audit explicitly used this worktree's private dependency environment, restored the owned API/test graph and found no vulnerable packages in all five own projects. A fresh owned build is required before the eventual serial full suite. No broad acceptance/whole-owner closure is inferred from these controls.

## Final serial gates and freeze

After root released the PostgreSQL slot, direct `dotnet build Legacy.Maliev.CatalogService.Tests/Legacy.Maliev.CatalogService.Tests.csproj -c Release --no-restore -warnaserror -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=<owned worktree>/.dependencies` succeeded with zero warnings/errors. Both exact private dependencies also built in Release under this worktree. Unfiltered `dotnet test Legacy.Maliev.CatalogService.Tests/Legacy.Maliev.CatalogService.Tests.csproj -c Release --no-build --no-restore --results-directory TestResults/catalog-context-full --logger "trx;LogFileName=full.trx"` reached 176 PASS, zero failures/skips, 1m18s. No build/format/audit overlapped the suite. This includes all existing tests and 13 NEW cases, not a partial filtered acceptance claim.

Whole format, corrected five-project transitive vulnerability audit, scoped redacted secret scan, and tracked/NEW-file whitespace checks passed. No full coverage threshold claim is made in this context/security slice. Three owned paths are root .dockerignore, NEW Integration/CatalogBuildContextParityTests.cs and this design. No runtime/factory/old assertion, workflow/schema/source/ledger changes; no commit/push/GitHub mutation or persistent/provider/deployment action.

Source `03eaff1194c3ae2a54ceefeae31deffaff90436f` receives only bounded recursive context-exclusion evidence here, not complete original ignore-rule or whole-owner acceptance. The four security source records receive existing factory-control proof only; historical scaffold/generated companion/startup/merge/global-owner obligations remain pending. Root retains independent review, integration and any future owner dispositions.
