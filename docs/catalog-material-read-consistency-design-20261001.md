# Catalog material collection consistency: issue31 candidate, 2026-10-01

## Authority and current phase

Initial TEST/DESIGN-only evidence is retained below. After terminal full RED and
static checks, root explicitly approved a bounded runtime candidate under
[Catalog issue31](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CatalogService/issues/31).
Current ownership supersedes the initial tests-only boundary: one application
file, exactly two policy-superseded historical test methods, the two new integration files, and
this document. Migrations, configuration, CI, ledger, contracts, other existing
tests, and other runtime are unchanged. No commits or external writes authorized.
Disposable test-container databases/cache only; no source SQL Server, production
data, persistent DDL, deployment, or IAM grants. Parent issues27/29 remain OPEN.

Owned worktree: `B:/maliev-legacy/.worktrees/catalog-material-cache-acceptance-20261001`.
Branch: `codex/catalog-material-cache-acceptance-20261001`.
Exact base: `a961b28f959c5814d7c2ff97c246c61843a88616` (merged PR30).
Both older Catalog worktrees remain untouched, root-owned/read-only.
The previous Catalog writer confirms it no longer owns Catalog files/outputs.

Current owned files are this document, two NEW integration files, and two narrowly
authorized existing files:

- `Legacy.Maliev.CatalogService.Tests/Integration/CatalogMaterialCollectionFailureHttpTests.cs`
- `Legacy.Maliev.CatalogService.Tests/Integration/MaterialCollectionFailureFixture.cs`
- `Legacy.Maliev.CatalogService.Application/Services/CatalogApplicationService.cs`
- `Legacy.Maliev.CatalogService.Tests/Application/CatalogApplicationServiceTests.cs` (only the cyclic material DTO unit method and required using)
- `Legacy.Maliev.CatalogService.Tests/Integration/CatalogHttpLifecycleTests.cs` (only the existing invalid-color-update method name, final GET local name and final expected value)

Fresh, private, clean CI-pinned clones reside in this worktree's `.dependencies`:
Defaults `003b255f0fb0f0bce032f5b5ff15d28be0c8c391`, Contracts
`78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. No sibling outputs are consumed.

## Committed-source traceability and owner disposition

All original reads use the read-only bare mirror
`B:/maliev-legacy/.artifacts/source-commit-mirror-20260930.git`, exact checkpoint
`bed10c7d15e0698e0b75f1329d0f312937f5d77f`. No original checkout/fetch/write.

| Source revision/path | Verified behavior | Current counterpart and bounded disposition |
| --- | --- | --- |
| `5fac706a7983a6d359b39acbd670e6800afe020e`, `Maliev.MaterialService.Api/Controllers/MaterialsController.cs` | `GetPaginatedMaterialAsync` queries `Material.Include(MaterialGroup).AsNoTracking`; printable/machinable queries filter the database directly | `Api/Controllers/MaterialsController.cs`, `Application/Services/CatalogApplicationService.cs`: common cached collection is filtered/paged after a cache-first read; real failure tests now prove stale results |
| Same full SHA, `Maliev.MaterialService.Api/Controllers/MaterialGroupsController.cs` | `GetAllMaterialGroupAsync` awaits `context.MaterialGroup.ToListAsync()` | Current `GetMaterialGroupsAsync` uses `GetListAsync` cache-first; RED update/delete/late-fill and embedded-group projection |
| Same full SHA, `Maliev.MaterialService.Api/Controllers/ColorsController.cs` | `GetAllColorsAsync` awaits `context.Color.ToListAsync()` | Current `GetColorsAsync` uses `GetListAsync` cache-first; RED update/delete/late-fill |
| Same full SHA, `Maliev.MaterialService.Api/Controllers/SurfaceFinishesController.cs` | `GetAllSurfaceFinishAsync` awaits `context.SurfaceFinish.AsNoTracking().ToListAsync()` | Current `GetSurfaceFinishesAsync` uses `GetListAsync` cache-first; RED update/delete/late-fill |
| `72eb9f1949176392141951d35e6e06f7c30af4c2`, the same four controller paths and `Maliev.MaterialService.Data/Database/MaterialContext/*` | Updated source model/startup checkpoint retains direct collection queries; no Redis collection authority was introduced | Current legacy fields/nullable commercial values and DTO projection remain untouched; this proposal changes read authority, not schemas or defaults |
| Latest `bed10c7d15e0698e0b75f1329d0f312937f5d77f`, the same controller paths | Materials controller's last path change remains full `5fac706a7983a6d359b39acbd670e6800afe020e`; direct database read semantics remain | Do not replay obsolete source additions or infer all historical owner records resolved |
| `1c611bb96d3a077090a8c65587cb7c4270af0aa4`, `7b4703576cf183abc09cf558148b5c8afb97d20c`, `bed10c7d15e0698e0b75f1329d0f312937f5d77f`, `Maliev.MaterialService.Common/InstantQuotationMaterialCatalog.cs` and API reconciler/tests | Additive ESD catalog provenance | Current PR30 implements producer reconciliation; default-off activation and cross-service acceptance remain separate issue29 gates, not changed by this slice |

Source Material-family ownership belongs to Catalog. Country/Currency source
records remain CountryService-owned despite the Catalog facades. This proposal
does not alter Countries, Currencies, exchange rates, Web pricing, quotation/order
mapping, catalog reconciliation, or any source ledger resolution.

## Initial RED boundary and evidence (historical, preserved)

Normal actual `Program` runs in **Production**, with real RS256 middleware,
fixture-specific issuer/audience/public key and signed employee subject. No fake
authentication, live IAM bypass, or repository/service substitution. Only the
existing exact route permissions are granted. Source JWT infrastructure and
production-issued capabilities are not asserted by fixture-generated tokens.

The fixture owns PostgreSQL `postgres:18.1-bookworm` and Redis `redis:7.4-alpine`.
Catalog uses its actual migrations; Country/Currency fixture schemas use their
existing model creation solely in disposable databases. Real normal contexts,
repository, application service, cache adapter, and controller/JSON pipeline are
used. Reconciliation stays explicitly **false**; host creation asserts it.

Removal failure uses Redis ACL denial of both **DEL and UNLINK**, not a fabricated
exception: reads/writes remain available, cached physical key presence is checked,
normal HTTP PUT/DELETE returns 204, and independent fresh PostgreSQL contexts
prove the mutation committed before the failing HTTP collection assertion.

Late-fill tests use two separately constructed Production hosts sharing these
actual containers. A test-only forwarding `IDistributedCache` decorator pauses
exactly the first selected SetAsync **after the real database snapshot has been
serialized**. While paused, the second host acknowledges a normal update and
successfully invalidates the key. Releasing the first host writes those old bytes
to actual Redis. The already-started read may return its original snapshot; the
subsequent normal GET must return committed state. It currently does not.
The decorator forwards every operation to the original registered Redis cache;
it does not provide authoritative state or replace caching/authentication logic.

Caller-abort control delays the real EF save boundary through a test-only
SaveChangesInterceptor, preserving actual request-token flow. Client cancellation
reaches the save barrier; no SQL save occurs, and independent DB/cache snapshots
remain identical. This is not a postcommit cancellation guarantee.

### Executed cases

27 new cases, final focused RED **14 failed / 13 passed / 0 skipped**:

- RED 4 acknowledged updates after real invalidation failure: materials/groups/colors/finishes.
- RED 4 acknowledged deletes after real invalidation failure: deleted rows return in collections.
- RED 4 paired-host late old fills after successful invalidation: subsequent GET returns Before, not After.
- RED 1 group update leaves stale embedded MaterialGroup in material collection.
- RED 1 Printable/Machinable update leaves changed material in filtered collections.
- PASS 4 actual Redis read denials fall back to current PostgreSQL.
- PASS 4 invalid length updates return 400 and preserve exact DB/cache snapshots.
- PASS 4 normal JWT controls: missing/wrong-signature 401; read-only/standalone wildcard 403, no effects.
- PASS 1 pre-save caller abort reaches request cancellation and leaves DB/cache unchanged.

Initial diagnostic run was 15 failed / 12 passed. Its extra failure was an
incorrect NEW test expectation that namespace wildcard `legacy-catalog.*` denies.
Pinned existing permission matcher intentionally supports namespace wildcards.
The NEW control now tests standalone `*`, which is denied for this employee subject.
Original diagnostic TRX is retained, no runtime authorization policy changed.

## Reviewed minimal repair (approved after terminal RED)

Root selected the conservative source-compatible policy: **PostgreSQL is authoritative
for these four material-owner collections**, including page/filter/nested group
projections. Remove cache-hit authority (and preferably unused collection fills)
from `GetMaterialsWithGroupsAsync` and from only the material group/color/finish
callers of the generic list helper. Preserve Countries/Currencies cache policy.
Keep all legacy routes, permission requirements, DTO fields, null serialization,
search/sort/pagination defaults, 404-empty behavior, CRUD, association semantics,
cache adapters, invalidation compatibility, and default-off reconciliation.

Approved runtime ownership: ONLY
`Legacy.Maliev.CatalogService.Application/Services/CatalogApplicationService.cs`.
No distributed cache generations, locks, schema changes, new retries, receipts,
startup DDL, or infrastructure activation. Failed/late fills cannot govern future
reads once these paths query the repository normally.

Existing-test audit: all 136 controls were unchanged in the RED phase. The only
existing assertion whose policy would be intentionally superseded if fills are
removed is `GetMaterialsAsync_CyclicEfGraph_CachesCompleteResponseInsteadOfNavigationGraph`
in `Tests/Application/CatalogApplicationServiceTests.cs` (lines81-122): retain its
commercial/nested DTO assertions, replace only the cache-fill contract with
source-authoritative cached-stale-vs-repository/no-read/no-set controls after
explicit root approval, now implemented. `GetCountriesAsync_CacheHit_DoesNotQueryRepository`
and group dual-invalidation assertions must stay unchanged. No broad cache-test
rewrites or assertion weakening are proposed.

The NEW tests now explicitly write independent literal frozen old-response bytes
to real Redis before failed-removal, invalid-input, denial, and cancellation
controls. Neither their expected response nor cache seed is generated by runtime
serialization. This preserves real cache snapshots after collection fills were
removed. The NEW late-fill test explicitly starts that literal old snapshot write
through the registered actual Redis forwarding decorator, pauses the write,
acknowledges the second host's mutation and invalidation, then releases the old
bytes into Redis and asserts subsequent normal HTTP reads fresh DB state. It no
longer waits for removed production SetAsync. Original27-case RED TRX and original
test/fixture hashes below remain historical evidence, not current byte hashes.

Rejected alternative no-cache-read/retain-fills would keep current tests unchanged, but
continues paying for unused Redis writes. A distributed generation/fencing cache
design is larger than this reference-catalog parity repair and not recommended
without a separate scope gate.

## Validation and acceptance boundaries

Executed Release direct Tests build: **0 warnings, 0 errors**, all project-reference
and private dependency outputs show `bin/Release/net10.0`. Unchanged baseline full
suite **136 passed / 0 failed / 0 skipped**. New final focused result is deliberate
RED, not acceptance. No test is skipped or excluded to manufacture success.

Commands (from owned worktree, the two properties on all build/test commands):

```powershell
dotnet build Legacy.Maliev.CatalogService.Tests/Legacy.Maliev.CatalogService.Tests.csproj -c Release -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/catalog-material-cache-acceptance-20261001/.dependencies
dotnet test Legacy.Maliev.CatalogService.Tests/Legacy.Maliev.CatalogService.Tests.csproj -c Release --no-build -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/catalog-material-cache-acceptance-20261001/.dependencies --logger trx --results-directory TestResults/catalog-cache-baseline
dotnet test Legacy.Maliev.CatalogService.Tests/Legacy.Maliev.CatalogService.Tests.csproj -c Release --no-build -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/catalog-material-cache-acceptance-20261001/.dependencies --filter FullyQualifiedName~CatalogMaterialCollectionFailureHttpTests --logger trx --results-directory TestResults/catalog-cache-focused-red-final
dotnet test Legacy.Maliev.CatalogService.Tests/Legacy.Maliev.CatalogService.Tests.csproj -c Release --no-build -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/catalog-material-cache-acceptance-20261001/.dependencies --logger trx --results-directory TestResults/catalog-cache-full-red
```

Retained ignored TRX directories: `catalog-cache-baseline` (136 PASS),
`catalog-cache-focused-red` (initial diagnostic), `catalog-cache-focused-red-final`
(14/13), `catalog-cache-full-red` (**149 passed / 14 failed / 0 skipped / 163 total**).
The unfiltered full run preserves all136 original passing controls plus13 new
passing controls; the only failures are the14 new material collection regressions.
Full TRX: `TestResults/catalog-cache-full-red/natth_MALIEV-31USFIV_2026-10-01_09_34_07_net10.0.trx`.

Whole solution `dotnet format --verify-no-changes --no-restore` exited0 with the
private dependency root supplied via environment. The unexcluded transitive
package vulnerability audit reported no vulnerable packages for all five Catalog
projects against current NuGet sources. `gitleaks dir` on the integration tests
scanned93.29KB and reported no leaks; `git diff --check` returned clean. Private
dependency HEADs remain exact, both private clones clean. Only the three NEW owned
files are untracked; no tracked/existing files changed. All build/test/static
sessions are terminal; outputs remain exclusively owned pending root review.

Historical RED new test SHA256: `C3E69561AF29A762839840374C6C5D7BCDB844502B256F819E4CB7FBCF96484C`.
Historical RED new fixture SHA256: `FBAF0DFA527F5209D4F314BE6CFA550AB113FBAA97EDA105D5BAC5E60C1D6488`.

After approved repair: fresh Release0W0E, focused all27 PASS, entire unfiltered
affected suite PASS, format/audits/scoped secrets/diff, no existing security or
source compatibility regressions. Any explicitly superseded single unit contract
requires separate permission as above.

Neither these disposable tests nor a green future repair constitute real Aspire,
production/cutover acceptance, an issue29 activation, or whole historical Catalog
owner completion. Catalog27's raw API coverage gate remains independent; no
threshold/exclusion/generated-code waiver or coverage improvement is claimed.

## Runtime candidate chronology and second old-policy gate

Approved minimal runtime changes are implemented in the sole application file;
Countries/Currencies continue using the untouched cache-aware generic helper.
Material collections use repository projection with no cache reads/fills, while
legacy mutation invalidation remains unchanged for compatibility with old writers.
Exactly the approved cyclic DTO unit contract and invalid-color HTTP read policy
are superseded. No other existing tests are changed.

Fresh direct Tests Release: **0 warnings / 0 errors**. Focus including the27 new
HTTP tests plus application/cache units: **37 passed / 0 failed / 0 skipped**,
TRX `TestResults/catalog-cache-focused-green/natth_MALIEV-31USFIV_2026-10-01_09_39_13_net10.0.trx`.
First unfiltered runtime full: **162 passed / 1 failed / 0 skipped / 163 total**,
TRX `TestResults/catalog-cache-full-green-coverage/natth_MALIEV-31USFIV_2026-10-01_09_39_45_net10.0.trx`.
All27 new regressions are green. This failed full is retained diagnostic evidence,
not accepted completion.

The additional old policy assertion is
`CatalogHttpLifecycleTests.InvalidUpdate_LeavesPersistedValuesAndPrimedActualCacheUnchanged`,
lines292-309 of the unchanged existing integration file. The test creates Black,
primes collection GET, directly changes actual PostgreSQL to Stored-only change,
then proves invalid whitespace PUT returns400 and PostgreSQL remains unchanged.
Its final assertion expects old Black from the subsequent GET. Authoritative
reads correctly return Stored-only change. No mutation or validation guarantee
failed. Root separately inspected and approved an exact narrow override after
this retained failed full: method renamed to
`InvalidUpdate_PreservesPersistedValuesAndAuthoritativeCollectionRead`, final GET
local renamed from retained to authoritative, final expected string changed to
Stored-only change. Create/prime Black assertion, direct PG update, invalid PUT400
and fresh persisted assertion are all preserved verbatim. This is a second
explicit historical-test policy override, not a relaxed mutation/validation test.

Unexcluded XPlat coverage (no custom runsettings/exclusions/threshold changes):
API **37.64%** line /28.47% branch; Application **95.85%** /76.92%; Data **98.83%**
/94.59%; Domain **100%** /100%. Raw API remains below80 and is not waived.
Report: `TestResults/catalog-cache-full-green-coverage/a0ba36bb-60a6-4e55-9daf-92ae260a27f2/coverage.cobertura.xml`.
Actual modules also retain Contracts/Defaults denominator entries; no exclusions
were added to hide generated OpenAPI or shared helper coverage.

Current runtime static checks: whole-solution format exited0; all five projects'
transitive audits reported no vulnerable packages; gitleaks Git history scanned30
commits without leaks and application/tests/docs directory scans reported no leaks;
tracked whitespace check clean. All sessions are terminal. A fresh full green
suite is still required after any separately approved old-policy correction.

## Final frozen handoff after second approved historical-policy override

Fresh Release direct Tests and both private dependencies: **0 warnings / 0 errors**.
Combined final focus (new HTTP tests, application units, cache units and the exact
approved historical HTTP case): **38 passed / 0 failed / 0 skipped**.
Final unfiltered full: **163 passed / 0 failed / 0 skipped**. Automated baseline
name mapping confirms all136 original controls are retained/passing, including
the two explicitly authorized policy supersessions; all27 new cases also pass.

Final TRX:

- `TestResults/catalog-cache-final-focused/natth_MALIEV-31USFIV_2026-10-01_09_45_50_net10.0.trx`
- `TestResults/catalog-cache-final-full-coverage/natth_MALIEV-31USFIV_2026-10-01_09_46_34_net10.0.trx`

Unexcluded coverage XML:
`TestResults/catalog-cache-final-full-coverage/aa8843e4-931b-449c-a48c-bdebcae229b8/coverage.cobertura.xml`.
API37.64%, Application95.85%, Data98.83%, Domain100% line coverage. API's separate
80% requirement remains unmet and not waived. No exclusion/runsettings changes.

Final validation commands use the same direct Tests project and absolute private
properties listed above, with final results directories as below:

```powershell
dotnet test Legacy.Maliev.CatalogService.Tests/Legacy.Maliev.CatalogService.Tests.csproj -c Release --no-build -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/catalog-material-cache-acceptance-20261001/.dependencies --filter 'FullyQualifiedName~CatalogMaterialCollectionFailureHttpTests|FullyQualifiedName~CatalogApplicationServiceTests|FullyQualifiedName~DistributedCatalogCacheTests|FullyQualifiedName~InvalidUpdate_PreservesPersistedValuesAndAuthoritativeCollectionRead' --logger trx --results-directory TestResults/catalog-cache-final-focused
dotnet test Legacy.Maliev.CatalogService.Tests/Legacy.Maliev.CatalogService.Tests.csproj -c Release --no-build -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/catalog-material-cache-acceptance-20261001/.dependencies --collect:'XPlat Code Coverage' --logger trx --results-directory TestResults/catalog-cache-final-full-coverage
```

Whole-solution format verification exited0 with the absolute private root in
environment. Current-source transitive audits for all five projects reported no
vulnerable packages. Git-history gitleaks scanned30 commits without leaks;
application/tests/docs scoped scans likewise reported none. Whitespace diff clean.
The earlier full163 failure and14-failure RED artifacts remain intact.

All six changed files are listed in the ownership section. No other tracked files
changed. Current runtime and test SHA256s:

| File | SHA256 |
| --- | --- |
| Application service | `25372118BA33B5B894ACE97357E66930115B311AFD01859E34140ADE868597E7` |
| Existing application unit file | `CA6666A0C2E1EA28815E5564E778C969338EAE3BA8DB1D8AEAE09A4C35A3C53D` |
| Existing HTTP lifecycle file | `F4A1132D2D20869DC4E036AEF68D3903E0C167A5F03E90BC4981D81F376DA5A3` |
| New HTTP regressions | `506873A7E52A1849105BFB8BFAA5F208D82606F68FE5A36D413433FF81F8A507` |
| New isolated fixture | `899B818EE8C6B5AAC925E48B850DDF94F99A205DA3EA5733310F35E5B90E960D` |

Private clones remain clean at exact CI pins. All build/test/static sessions have
terminated. Candidate files/outputs are **frozen and released to root** for
independent acceptance: no more agent edits/builds. No commit/push, production
data/schema activation, configuration change, deploy, ledger or GitHub mutation.
Issue31 is bounded collection authority; parent27/29 and source-owner partial
status are not closed by this evidence. Real Aspire/production-derived acceptance
and coordinated reconciliation activation remain distinct gates.

## Root independent acceptance

Root independently reviewed all six files, both narrowly approved historical stale-cache policy supersessions, the real Redis ACL failures/late-fill transport and fresh PostgreSQL persisted-effect assertions. Final direct Release graph: **0 warnings / 0 errors**. Combined focus: **38 passed / 0 failed / 0 skipped**, `TestResults/root-catalog-focus/root-catalog-focus.trx`. Complete unfiltered affected suite: **163 passed / 0 failed / 0 skipped**, `TestResults/root-catalog-full/root-catalog-full.trx`. Independent unexcluded report `TestResults/root-catalog-full/f903451e-f5b5-449a-adfa-1615d4269a0e/coverage.cobertura.xml` confirms API37.64/Application95.85/Data98.83/Domain100; API80 remains an open quality gate, not waived. Whole-solution formatting and all five transitive package audits completed successfully. Test/build/private dependency artifacts are retained as ignored review evidence, never staged. Required protected CI and exact-main verification remain integration gates; no source-owner blanket closure, production-derived Aspire acceptance or reconciliation activation is implied.
