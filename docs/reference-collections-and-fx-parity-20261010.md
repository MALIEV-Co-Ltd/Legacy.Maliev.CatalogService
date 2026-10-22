# Authoritative reference lists and literal exchange queries

Country and Currency collection requests now read their owning repositories on
every request. A failed Redis invalidation or a late fill from an older reader
therefore cannot hide an acknowledged database write. Country sorting, Currency
repository order, response fields, anonymous lists, protected detail/write
permissions, and empty-list HTTP 404 behavior are retained.

The Frankfurter client escapes the supplied currency strings without trimming or
changing case. This preserves the original upstream query values while retaining
the registered resilience pipeline, response shape and provider failure handling.

The private source witnesses are the Country and Currency controllers and the
Currency ExchangeRates controller at source commit
`5fac706a7983a6d359b39acbd670e6800afe020e`. No private history or configuration is
copied into this repository. This implementation reuses the reviewed reference
successor published as source intake in PR #75; it integrates only the reference
list and exchange-query changes against protected main
`1b516d712505e6a648add4c4adc3e9fcc415eca1`.

## Regression coverage

Eight new Country/Currency HTTP cases exercise failed Redis invalidation after
create/update/delete, last-item 404, two-host late old fills, literal database
reads, ordering, and rejected writes. They reuse the existing PostgreSQL/Redis
Testcontainers fixture. Fixture adapters add owning-database contexts, reset the
two reference tables, and grant the test writer granular reference permissions;
existing container construction, startup and disposal remain intact.

Four new exchange HTTP rows cover lower, upper, mixed-case and padded query
values through the real controller, JWT middleware and loopback provider. The ten
existing exchange HTTP cases remain. Two existing expected queries now assert
literal input preservation. The full-suite forecast increases from 355 to 367;
this is an inventory forecast, not an executed result. All original twenty
Country/health diagnostic exports and coverage thresholds remain required.

The late-fill update uses a nonempty padded literal accepted by the current write
validation. Empty-string write compatibility, material candidates, capsule
activation and canonical CountryService ownership are outside this change.

## Validation status before draft publication

Local Release build, focused .NET tests, full suite, .NET format and package
vulnerability audit: **NOT RUN**. Fresh memory admission failed the unchanged
4,194,304 KiB floor (the coordinator observed 2,042,268 KiB with no SDK workers).
The owner-approved 2026-10-10 migration exception permits this source-reviewed
draft publication before those checks; it does not waive any check.

`git diff --check` passed. The existing synthetic diagnostic-reader controls
passed 31 cases and reported temporary fixtures removed; these are parser
controls, not native HTTP/database tests. A working-tree Gitleaks scan reported
zero findings. No local SDK, PostgreSQL, Redis or browser worker was launched.

Before protected merge, hosted validation must pass on the exact proposed head:
Release build with zero warnings/errors, the fourteen exchange HTTP and eight
reference HTTP cases, the full 367-case suite and original diagnostic exports,
format/static/security checks, contracts and unchanged coverage gates. After
merge, verify the exact protected-main SHA and its required checks. Source review
and this draft do not establish runtime parity, deployment or database cutover.

The first draft head `422dc0e64b0ccef765efa43408d425cff301d253` was exercised by
hosted PR run `38041436747`: Release build had zero warnings/errors; the full
suite executed 367 cases with 366 passing and one failing. The sole failure was
the existing unit test expecting a country cache hit to bypass its strict
repository mock. Its expectation is corrected to require the owning repository's
state even with a conflicting stale cache value, verify one repository read and
verify no cache calls. This does not change the test count or production code.
Hosted validation on the corrected head remains required; the failed first run
does not qualify the change.
