# Catalog lookup v1 implementation candidate

These definitions describe the implementation candidate, not production availability.
The original Catalog APIs, SQL Server source and cutover gates are preserved.
No customer/supplier addresses are stored here and no database initialization is performed.

## Identity and connection

Catalog serves `/api/v1/thai-addresses` with `legacy-catalog.locations.read` and
`/api/v1/companies/search` with `legacy-catalog.companies.read`. Both require JWT
authentication through existing ServiceDefaults. There is no anonymous Catalog route.
The Web server workload calls Catalog using its server-held service identity; browser
principals must not receive these permissions or tokens. Intranet calls through its
existing authorized BFF. Consumers use the existing Catalog service connection;
no new service process or database is required. AppHost/IAM registration and joined
consumer evidence remain separately owned and pending.

All lookup routes share a per-principal, per-instance limit of 60 requests/minute,
no queued requests, 429 when exceeded. Public Web adapters need their own bounds
and rate limiting because a service identity aggregates callers. Existing CSRF,
locked fields, login and save permissions stay with the consumer.

## Hierarchy and narrowing

GET routes: `/provinces`, `/districts`, `/subdistricts`, `/postcodes`, `/autocomplete`
under `/api/v1/thai-addresses`. Every route accepts `q`, `provinceCode`, `districtCode`,
`subdistrictCode`, `postcode`, `limit`, `cursor`. All filters combine with AND.
`q` is at most 128 characters; each whitespace-separated name token must match a
Thai/English name or reviewed alias, or a postcode. Administrative labels are stripped
before splitting query tokens on whitespace. This is substring filtering,
not weighted scoring. Administrative codes are ASCII strings of 2/4/6 digits.
Postcodes are 5 digits, accepting Thai digits normalized to ASCII. `limit` is 1..50,
default 20. Unknown codes and inconsistent parents are 400; valid no-match is 200.
Missing/corrupt dataset is 503. Each page is:

```json
{"datasetVersion":"...","items":[],"hasMore":false}
```

`nextCursor` is present only when another page exists and binds the dataset,
route, filters and page size. Consumer follows it with unchanged filters. Lists
return distinct administrative entities `{code,nameTh,nameEn}`; district entities
also have `provinceCode`, subdistrict entities have `districtCode`. Postcodes are
strings. Autocomplete items are `{province,district,subdistrict,postcode}` using
those entities. Postcode-first returns every combination through paging; never
assume one subdistrict. The engine supports multiple postcode relationships,
although the current upstream snapshot supplies one per subdistrict.

## Pasted text

POST `/api/v1/thai-addresses/resolve`, body `{text,constraints?}`; `text` is 1..2048
characters, body limit 16384 bytes. Constraints use the code/postcode properties
above; `q` and `cursor` are rejected. Response properties:
`datasetVersion`, `originalText`, `normalizedText`, `outcome`, `candidates`, `hasMore`,
`uniqueFields`, `detailText`, `extractedSpans`, `conflicts`.

`outcome` is `exact`, `ambiguous`, `not-found` or `conflict`. Candidates preview at
most 50 combinations. Unique fields are computed over the complete match set.
Unknown unique fields are omitted; missing input postcode is not inferred into
`uniqueFields`, even if candidate data contains one. `conflicts` is an array of
field identifiers (province/district/subdistrict/postcode), a refinement of the
initial proposal's reason objects. Unknown explicit names and contradictory
constraints are conflict, with no autofill fields.

Thai digits normalize without changing UTF-16 offsets. Thai prefixes ต./อ./จ.,
ตำบล/อำเภอ/จังหวัด, แขวง/เขต and English Tambon/Amphoe/Province/District/Subdistrict
are recognized, including English suffix labels and spaced Thai abbreviations.
Bare province/Bangkok aliases are recognized unless the name is explicitly used as
a road/building name; bare district and
subdistrict names are deliberately retained because they may be street/building
text. Postcode extraction is restricted to a final standalone five-digit token,
optionally followed by Thailand/ประเทศไทย. Input with no confident place tokens
is not-found. This is conservative administrative extraction, not arbitrary address
understanding, deliverability or house-existence validation.

Spans `{start,length,kind,text}` refer to unchanged `originalText` in UTF-16 code
units. Only confidently extracted administrative spans are removed from `detailText`;
house/unit/building/moo/soi/road text is retained. On conflict/not-found detail stays
unchanged. The consumer previews changes and preserves entered detail until reviewed.
Never log raw addresses or use resolver bodies as ordinary cache/log keys.

## Company suggestions

GET `/api/v1/companies/search?q=...&queryType=name&language=th&limit=20`.
Name query is 2..128 characters, language `th|en`, limit 1..50. Tax-ID queries require
13 digits (Thai digits normalized to ASCII) and return only exact tax-ID matches from
the same suggestion endpoint. Invalid requests return 400. Disabled/malformed/failed provider
returns 503 `unavailable`; provider throttling or local rate-budget exhaustion returns
429 `rate-limited` with a retry delay. No-match is 200 with empty items.

Response `{outcome,provider:"creden",capability:"suggestion",items,hasMore}`. Items
have `nameTh`, `nameEn`, `taxId`, `retrievedAt`, `status`, `companyType`, `objectives`,
`registeredAddress`. All unknown company fields are explicit null. Suggestion-only
fields never imply registered address, active status, business objectives or type.
Truncation sets hasMore but provider continuation is unsupported; narrow the query.
CustomerService remains persistence owner; manual entry/correction must remain available.

Live requests on 2026-10-06 confirmed Thai/English names and tax-ID search without
credentials, including MALIEV tax ID 0125561001573. Successful no-match responses use
`data.result: {}`; the adapter accepts an empty object or empty array as no-match.
Nonempty objects, null and malformed results remain unavailable. These checks verify
observed endpoint behavior and do not establish a provider availability guarantee.

The packaged service configuration enables Creden (`Creden:Enabled=true`) at the owner's
explicit request on 2026-10-06. `Creden:Enabled=false` remains an operator kill switch;
the options class alone still defaults to disabled. `AccessReviewReference=null`,
`BaseUrl=https://data.creden.co/`, `TimeoutSeconds=3` (1..10), `CacheSeconds=300`
(1..3600), `RequestsPerMinute=30` (1..60). `AccessReviewReference` is optional review
metadata; `Enabled=true` explicitly enables the owner-approved integration. HTTPS origin is restricted,
redirects are disabled, response <=65536 bytes, one attempt, one in-flight request
per instance, zero waiting queue. Rate caps are per instance; deployment must bound
replica totals to agreed provider limits. Successful/no-match cache keys use
`legacy:catalog:creden:v2:` plus a hash binding query type, language, limit and query;
name and tax-ID cache entries are separate. Failures are not cached. Caller cancellation
propagates; own timeout is unavailable. No provider credentials or raw queries are
logged by this adapter. Live behavior is confirmed above; a published service contract
and provider rate-limit guarantee have not been established.

## Dataset provenance and controlled updates

### Provider quota research and fallback

On 2026-10-06 the public Creden Data and Creden corporate pages were checked for a
published quota for `sapi/search/get_suggestion`; none was found. One ordinary
suggestion request returned HTTP 200 with no Retry-After or rate-limit headers.
This does not establish an unlimited quota. The configured 30 upstream requests
per minute is our per-instance protective budget, not a stated Creden allowance.
Sources: https://data.creden.co/ and https://creden.co/.

Provider HTTP 429 and exhaustion of our local budget return HTTP 429 with
`outcome=rate-limited`, empty items and `retryAfterSeconds`, plus a Retry-After
header. Provider Retry-After seconds or dates are respected up to a bounded
24-hour cooldown; absent, malformed, zero or past values use 60 seconds. During
cooldown uncached queries do not reach Creden; valid cached results remain usable.
There are no immediate retries and no cached successful-looking throttle result.
Transport/provider outages remain HTTP 503, not no-match. Frontends must preserve
manual fields and explain rate limiting. Intranet already maps HTTP 429 to its
localized RateLimited feedback; Web's follow-up adds a distinct Thai/English message.
Enabling packaged configuration does not change deployed runtime settings.

`Data/ThaiAddresses/geography.json` is a pinned MIT-licensed snapshot with its original
LICENSE and provenance manifest. Revision and checksum are enforced by the loader.
The source is [Thailand Geography JSON](https://github.com/thailand-geography-data/thailand-geography-json).
Names, stable codes, parents, duplicate combinations and postcode syntax are checked.
This is independently maintained open data, not an authoritative deliverability feed.

Update in a review branch: pin a source revision; download the original file and license;
run `scripts/review-thai-dataset.ps1 -CandidateFile <file> -RetainedFile <reviewed-export>`;
review additions/removals/changed names and postcode relationships; record alias/correction
evidence, license/revision/date/SHA256 and counts; update loader constants and manifest;
run full hosted build, tests, formatting, vulnerabilities, secret/coverage checks;
obtain consumer/AppHost acceptance before production release. Retained export is a
reviewed non-customer array in the same geography schema; keep it out of the public repo.
Missing retained export is reported pending and does not imply parity. No script connects
to production or updates a database. Initialization is packaged local data, no `--seed`.
Rollback restores the previous reviewed artifact/constants as a coherent release.

Release is still held for retained LocationData reconciliation, reviewed additional
postcode coverage, consumer persisted-address/company flows,
and AppHost/IAM evidence. Hosted tests prove only their named service/HTTP boundaries.
