# Material ignored audit-date input parity

## Retained source ownership

Initial original commit `5fac706a7983a6d359b39acbd670e6800afe020e`, parents `[]`:

- `Maliev.MaterialService.Data/Database/MaterialContext/Material.cs`, blob `cc5f9cb965465e593d51634964b9f59b75a91625`: explicit nullable `DateTime?` CreatedDate and ModifiedDate.
- `Maliev.MaterialService.Api/Controllers/MaterialsController.cs`, blob `bc7b3712c109e437ba704a0ab5b31edbfd24ef45`: create (line 52) assigns server UTC creation/modification dates; update (line 406) preserves creation and assigns server UTC modification. Incoming audit dates are typed during binding and never copied.
- `Maliev.MaterialService.Api/Startup.cs`, blob `a0314f7ed6ff7d5a778f98d636d37c04b66e8bdf`: Newtonsoft input with NullValueHandling.Ignore and ReferenceLoopHandling.Ignore.

Retained model update `72eb9f1949176392141951d35e6e06f7c30af4c2`, parent `023aa2f143fa5a8f557d72c1302b9add64792d6a`, has Material blob `da7c3a45a56386ec13cd962f43b0a7216a7d8929` and preserves both nullable date types. The controller blob is unchanged. These are bounded field/action obligations, not a latest-checkpoint or whole-original-source closure claim.

## Current gap and change

Base Catalog commit `b463ca411b24f4bc7df65b4660aa0783b2dd1f25`, tree `f5decb4f2ee6670c2599d952efa822fb863766b2`. Its Material wrapper (blob `57a121914c5e6df7b92665ed9a14fdd46c89d57f`) validates ignored Id but skips malformed CreatedDate and ModifiedDate as unknown members.

The wrapper now validates every matching audit-date occurrence with the same invariant Newtonsoft ReadAsDateTime mechanism already used by the accepted lookup wrapper, then discards the field. Matching follows the caller's existing case-sensitivity option. No request/response property or client timestamp authority is added; serialization/shared options and all other parser branches remain unchanged.

This is only the two scalar audit-date fields of issue #102. MaterialGroup and the three navigation collections, recursive/element models and financial typed fields remain held. No price/currency input, schema, repository, service, route, auth/permission, graph write, association action, provider, FX or financial execution behavior changes. Comment/TrackingNumber and all other holds remain blocked.

## Regression inventory

New `MaterialIgnoredAuditDateSourceHttpTests` contains 62 authored cases:

| Method | Rows |
| --- | ---: |
| ValidTypedDates_AreIgnoredWithoutGrantingClientTimestampAuthority | 10 |
| InvalidTypedDates_ReturnBadRequestBeforeAnyMaterialWrite | 32 |
| InvalidDuplicateOccurrence_CannotBeHiddenByValidOrNullDates | 12 |
| MissingMaterial_PreservesTypedDateBindingBeforeNotFound | 2 |
| DateValidation_DoesNotGrantAnonymousOrReadOnlyWriteAuthority | 4 |
| Wrapper_RespectsCaseSensitivityAndDoesNotAddWritableDatesOrChangeSharedOptions | 2 |

The tests use the existing normal Program/RS256/PostgreSQL fixture without substituting authority, controller, service or repository. They submit only group/name/audit-date inputs, compare selected original nullable-date parsing, and verify fresh stored values, server dates, unchanged row counts and GET values. The local reference model deliberately contains only the two proven audit-date properties and selected settings; it is not a complete original host/model or original HTTP-error oracle.

The existing Material ignored-input workflow appends only this class to its filter. The existing full-suite forecast changes from 4593 to 4655 (=4593+62); the twenty Country/health diagnostic cases and strict counters/GUID joins, raw coverage requirements, parser and coverage thresholds stay unchanged. Forecasts do not establish executed results.

## Validation and resources

Author-side: retained source SHA/parent/path/blob and two model date types; complete original write-action mapping; exact reversible source literals; immutable blob readback/tree/inverse checks. Release build, 62 new cases, focused/full suites, formatting, static/security, actual HTTP/database behavior and raw coverage are **not run author-side**. Root owns GitHub Actions publication, exact-head evidence and protected-main acceptance under the unchanged gates.

No native SDK/test host, container, database, cloud, official-log, ref or deployment operation was performed by the author. No helper resources were allocated.
