# Exchange-rate provider parser source bundle

Base: protected Catalog main `acd4cc634c091e9982e7bfbc245e53c77304586e`, tree `8879ebb7ae10c56e1ac568edd470351552827ada`.

Retained original source witnesses: commits `5fac706a7983a6d359b39acbd670e6800afe020e` and `72eb9f1949176392141951d35e6e06f7c30af4c2`; final source checkpoint `770cb58032969ae4a0fe169686b53ab61a44fff8`.
Only the scoped Currency controller, model and Startup were read.
Controller blob `6ba9ae7b1469b539d271040974a2a16fa2bfcdac` calls stock `JsonConvert.DeserializeObject<OpenRatesModel>` and returns `ActionResult<OpenRatesModel>`.
Model blob `683d19951609e926c18a6548cf9c0babb5174abe` has Base string, nonnullable DateTime Date and Dictionary<string,string> Rates, with no initializers or required metadata.
Startup blob `565ff564241fdea5ebf7f98f59a0e86b41aa90c1` configures Newtonsoft null/reference-loop ignoring.

## Coherent change

Use the same stock parser and a private three-property projection only inside the existing provider adapter.
Do not install Newtonsoft as Catalog's global MVC serializer.
Explicit runtime Newtonsoft.Json 13.0.3 matches the declared minimum of the existing test MVC.NewtonsoftJson 10.0.3 dependency:
https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.NewtonsoftJson/10.0.3
https://www.nuget.org/packages/Newtonsoft.Json/13.0.3
No packages.lock.json or central package lock exists in this base tree; none is fabricated.

Allow a null provider model and null Rates through the interface and response model.
The controller returns NoContent for a null model and advertises 204; nonnull responses retain Catalog's accepted PascalCase.
Rates becomes nullable and optional in served OpenAPI. Date stays nonnullable and required.
Base null/omission is still never replaced with the requested currency.
No query normalization, permissions, resilience, provider origin, database, workflow, coverage threshold or Web code changes.

## Facet disposition

| Facet | Before this proposal | Proposal and evidence status |
| --- | --- | --- |
| Exact route and query literals | Accepted earlier, existing signed route cases | Preserved; retained tests rerun on exact hosted head |
| Anonymous/wrong/exact permission | Existing 401/403/success component cases | Preserved; no provider call without exact grant |
| String rate scalars, exponents, booleans and null | Accepted in #107 | Same stock parser; existing scalar cases retained |
| Provider Base identity/null | Accepted in #108, fresh-main 4512 tests passed | Same source semantic, no fabricated THB |
| Missing Date, empty/null/invalid Date | Missing date incorrectly threw | Stock nonnullable DateTime model; oracle compares results/errors |
| Missing/null/empty/invalid Rates | Missing/null incorrectly threw | Stock dictionary; null Rates omitted on wire |
| Case variants of typed property names | Manual lookup was case sensitive | Stock property matching, explicit matrix |
| Duplicate typed properties and rate keys | Manual dictionary construction could throw or replace incorrectly | Stock overwrite/population, explicit matrix |
| Number/boolean/structured Base | Manual GetString rejected scalar coercions | Stock typed string binding, explicit matrix |
| Root null/invalid root | Manual object access rejected null | Original-style loopback MVC action determines 204/error |
| Date formats, offsets, Microsoft dates | Manual GetDateTime had narrower grammar | Stock parser, Date and Kind comparison |
| Unknown members, comments, trailing commas | Hand parser differed | Stock parser, explicit matrix |
| Supplied structured rate values | Existing failure controls | Failure category updated from System.Text.Json to source Newtonsoft only; remains failure |
| Target PascalCase | Established accepted Catalog/Web contract | Preserved explicitly; source MVC naming disposition remains open |
| Provider failures, privacy and resilience | Existing component cases | Unchanged and required in exact hosted validation |
| Actual Web/provider/financial join | Not proven by Catalog unit/component tests | OPEN; held Web packet stays inactive |
| Whole issue #40 / whole original commits | Partial | Not closed by source preparation or authored forecast |

The actual source-style MVC oracle reuses the existing disposable loopback provider host.
It uses the retained ActionResult signature and Newtonsoft settings. Its property names are explicitly mapped to Catalog's accepted casing for comparisons; this is not a byte-identical source wire claim.
No real provider, authentication service, database or financial operation is contacted.
The existing production Web consumer at `78042c531cab0019f39fbe65fd6ee75ed47d1a29`, blob `0aab8b5782762e361044e3bec276de71a9b9b9f0`, already rejects null Rates and uses retrieved time for default Date. It is inspected only and remains unchanged.

## Validation

35 literal matrix rows are authored in each of the existing client and HTTP test classes: 70 new cases.
The fullForecast changes 4512 to 4582 as an expected inventory, not an execution result.
No GUIDs, passed counters, exclusions or coverage waivers are invented.
The prior date-only proposal is superseded, not published separately.

Local restore/build/test/format/audit: NOT RUN, below the existing resource admission floor and hosted-only lane.
Required hosted exact-head validation: Release build zero warnings/errors; both changed test classes and served OpenAPI test; full affected suite; formatting; package/history/current-tree scans; unchanged native inventory/all16 counters/exports; all four existing 80% coverage floors. Normal protected review/merge followed by fresh-main checks remains required.
No task-owned native processes, listeners, containers or volumes were started.
