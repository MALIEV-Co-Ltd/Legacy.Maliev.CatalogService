# Catalog retained Docker context exclusions

## Bounded provenance

This slice completes the Catalog root-context pattern transfer from original
`03eaff1194c3ae2a54ceefeae31deffaff90436f`, parent
`5b6d94a02a7578783e0980af10ecd36856aba024`. The retained service-local ignore
blob is `154abec4bdfb034fc76ea90bb6196b98b0ca4923` (31 patterns).
Catalog consolidates the original Material and Currency APIs; original Country
is separately owned. No Country or Procurement acceptance is duplicated.

Proposal base: Catalog main `e3c09ccd95e4ad1809f30fc473ecbdc6c2a57ca3`,
tree `10e7a967659a981e4c4f15a0a14aecbbe45ae845`.
The root ignore preimage `651d9c7877e80371805949ab2ad2804754dd019b`
has 17 rules. PR #36 preserved a bounded four-pattern repair. This slice
preserves every preimage rule and appends the 25 literally absent recursive
source patterns. Literal absence is source evidence, not a claimed Docker RED.
The resulting 42 rules include all 31 retained patterns and existing private-key
exclusions.

## Actual build-input boundary

The current Dockerfile `72fb2336b5be75935156526f4b33059f7d1ec3fb`
uses the repository root as context. It copies nuget.config, Directory.Build.props,
.dependencies and all four Catalog production projects before restore, followed
by COPY and publish. Current build/publish workflows pin ServiceDefaults to
`7edcd961024868513fd5f373cab3dcb261197f77` and CompatibilityContracts to
`78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`; these replace the historical
ServiceDefaults pin quoted in the earlier PR #36 design.

The current production project references and both pinned dependency trees,
projects and Directory.Build.props were inspected. No explicit production input
requires one of the added excluded extensions. The regression copies actual
production C#, project and resource files (excluding generated bin/obj), root
restore/build inputs, pinned dependency props/SDK/package metadata and READMEs,
and the explicitly published Thai address JSON/provenance/LICENSE files. Each
admitted file must survive the real Docker context with identical bytes.
No custom glob evaluator substitutes for Docker.

Negative fixtures cover every source category at root and nested depth. The
Dockerfile.dockerignore category is tested in two nested locations: putting a
fixture beside the scratch Dockerfile would override the root policy under test.
The ten previous negative paths and six previous actual-file controls remain
covered. Existing pfx/key/pem exclusions also receive root negative controls.
There are 69 negative paths in total. Synthetic input/output is removed in
finally on either success or failure. Existing Docker endpoint admission and
bounded process helper remain unchanged.

## Required hosted validation; not yet executed for this proposal

Keep heavy validation on GitHub Actions. No local build, Docker, test host,
database, provider, financial test execution or native helper is admitted here.

Before acceptance on the exact proposed head:
1. Release build with zero warnings and zero errors.
2. Focused CatalogBuildContextParityTests, including the real Docker/BuildKit
   FROM scratch/COPY test with network disabled, no cache and no skipped case.
3. Relevant suite and all existing required checks under the coordinator's
   financial-stop admission rules; no held provider/FX packet activation.
4. Existing formatting, dependency/security, contract and coverage gates with
   unchanged thresholds.
5. Separate exact protected-main acceptance after any protected merge.

A retained-rule baseline Docker run may demonstrate the new negatives fail;
until it executes, no RED/GREEN count is claimed. Published candidate/main
checks and original evidence must be retained by their designated collector.
No CI dispatch, ref/commit/PR publication, canonical tracking write, deployment,
dependency-pin update, database change, runtime/DTO behavior change or secret
configuration belongs to this source proposal. Comment/TrackingNumber and all
financial/provider holds remain blocked. Passing this bounded context slice
does not establish whole-source or whole-owner retirement.
