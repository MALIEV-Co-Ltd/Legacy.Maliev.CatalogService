# Country original mutation input boundaries

## Retained source ownership

Initial original commit `5fac706a7983a6d359b39acbd670e6800afe020e`, parents `[]`:

| Owner path | Blob | Bounded obligation |
| --- | --- | --- |
| `Maliev.CountryService.Data/Database/CountryContext/Country.cs` | `467f3755753ecac98d7cfdb8dc9d69e54f73cc9f` | Id is int; CreatedDate and ModifiedDate are DateTime?. |
| `Maliev.CountryService.Api/Controllers/CountriesController.cs` | `2ebceeee7ed09231019758d69843f5ebcc77f7bc` | POST50/PUT149 explicitly reject null before construct/lookup; PUT rejects ID zero; incoming Id/dates are not copied; create uses server UTC dates and update preserves creation/sets modification. |
| `Maliev.CountryService.Api/Startup.cs` | `2d01b68a23ce53b67941660b8f5006b6ffee9ca8` | Newtonsoft NullValueHandling.Ignore and ReferenceLoopHandling.Ignore. |

Model update `72eb9f1949176392141951d35e6e06f7c30af4c2`, parent `023aa2f143fa5a8f557d72c1302b9add64792d6a`, model blob `9e4229092328bad7ce56bb9d446e0b178326648d`, keeps all three typed metadata properties; the controller blob is unchanged. These bounded field/action obligations do not establish latest-checkpoint or whole-original closure.

## Current gap and change

Base `f017b3014891b2b9696d542d33be9eec246dda42`, tree `6bf05d3cc68cf8554112aee35052cf04da940aac`, is the Root-published Material audit-date draft PR #117, pending hosted acceptance when this packet was prepared. Its changes must be retained or explicitly reconciled onto the accepted base.

Current Country POST/PUT omit the original callable null checks, and the five-property request DTO silently skips malformed ignored Id/CreatedDate/ModifiedDate.

A Country-only request converter validates every matching ignored metadata occurrence with the existing invariant Newtonsoft ReadAsInt32/ReadAsDateTime precedent, then discards it. It respects existing case-sensitivity options, delegates the same five writable fields to their unchanged converters/validation, preserves writing and does not mutate shared serializer options. Registration applies only to MVC Country requests.

The controller restores empty 400 on null before service dispatch and retains the zero-ID update guard. This is callable-controller parity; existing ApiController can already reject HTTP JSON null. There is no observed HTTP-500 or complete original HTTP-error parity claim.

Five writable request fields, camelCase Country response attributes, routes/Location, anonymous-list-only policy, granular permissions, server dates, services/repositories and independent Country database ownership remain unchanged. No ISO mapping, schema, migration or updater change is included. Material and its PR #117 date slice, Currency/FX, financial/provider execution, recursive navigation and every other held boundary remain unchanged. Comment/TrackingNumber stays blocked.

## Authored regressions and enrollment

New `CountryOriginalNullControllerTests`: seven strict-mock cases (create-null; update-null for 0/101/int.MaxValue; unchanged zero-ID rejection; non-null found/not-found delegation preserving request identity and cancellation).

New `CountryServerMetadataSourceHttpTests`: 85 cases:

| Method | Rows |
| --- | ---: |
| ValidTypedMetadata_CannotControlStoredIdentityOrServerDates | 10 |
| InvalidTypedMetadata_ReturnsBadRequestBeforeAnyCountryWrite | 48 |
| InvalidDuplicateOccurrence_CannotBeHiddenByValidOrNullMetadata | 18 |
| MissingCountry_PreservesTypedBindingBeforeNotFound | 2 |
| MetadataValidation_DoesNotGrantAnonymousOrReadOnlyWriteAuthority | 4 |
| Wrapper_RespectsCaseSensitivityAndKeepsWritableShapeAndSharedOptions | 2 |
| ServedOpenApi_KeepsCountryRequestsFreeOfWritableServerMetadata | 1 |

Normal Program/RS256/PostgreSQL tests verify fresh Country-role values, unchanged storage/row counts after rejection, server timestamp authority, GET values and absence of consolidated Catalog Country rows. The selected three-property original reader is deliberately not a complete original host/model oracle. No authority middleware, service or repository is substituted. The OpenAPI check uses the existing metadata host and asserts no database work.

The existing focused workflow appends only the two exact new classes, retaining Material and lookup classes. FullForecast advances only 4655 to 4747 (=4655+92); the twenty Country/health diagnostic methods/cases, strict GUID/counters/raw coverage requirements and thresholds remain unchanged. The historical sealed candidate capsule policy remains untouched. Forecasts are not executed results.

## Acceptance and resources

Author-side checks are source-only: bounded original SHA/parent/path/blob reads and explicit types/action mapping, exact unique forward/inverse literals, immutable readback and exact tree/inverse proof. Release build, 92 new cases, focused/full suites, formatting, static/security, actual OpenAPI/HTTP/database behavior and raw coverage are **not run author-side**. Root owns publication, exact-head GitHub Actions evidence and protected-main acceptance under unchanged gates.

No native SDK, test host, container, database, cloud, official-log, ref, original-checkout write or deployment operation was performed. No helper resources were allocated.
