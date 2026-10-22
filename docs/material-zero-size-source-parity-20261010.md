# Explicit-zero Material pagination

An explicit `GET /materials?size=0` now produces empty items and HTTP 404 instead
of returning one material. The original source controller at
`5fac706a7983a6d359b39acbd670e6800afe020e` passes the requested size to
`PaginatedListWebApi.CreateAsync`; that helper calls `Take(pageSize)`, and the
controller returns 404 for empty items.

Private source association, without copying private code or history:

| Source path | Git blob |
| --- | --- |
| `Maliev.MaterialService.Api/Controllers/MaterialsController.cs` | `bc7b3712c109e437ba704a0ab5b31edbfd24ef45` |
| `Maliev.Entities/ViewModels/PaginatedListWebApi.cs` | `9684bc3f0ba2cd399621b3891c00b1b05ba6b398` |

This narrow implementation reuses the immutable explicit-zero source candidate
published through PR #74, integrated against accepted protected main
`6b5ecdd6adf0653dd74031a6f9e2274e3670466a`. Only the zero-size item projection
changes. Omitted and positive sizes, index clamping, sort/search behavior,
response shape, group inclusion and permissions retain their current behavior.
Negative-size semantics remain a separate source obligation; this change does
not claim complete pagination parity or alter the shared original helper.

Four new real HTTP/PostgreSQL cases use the existing
`MaterialCollectionFailureFixture` unchanged. Test consumers adapt the prepared
candidate's fixture calls to its current contexts and signed clients. The graph
contains three materials and retained color, finish and supplier associations;
read-only graph and row snapshots must remain identical after all requests.
Three rows exercise explicit zero with omitted/positive indexes and a matching
search. The fourth verifies omitted/positive sizes, metadata and group wire
shape, anonymous/wrong-grant rejection, past-end/no-match 404 and unchanged
stored data. No new fixture or resource framework is introduced.

The full-suite inventory forecast is 367 to 371. All original Country/health
diagnostics and exports, assembly coverage floors, static/security checks and
workflow requirements remain intact. Counts are not runtime results until
executed.

Local SDK validation requires immediate unchanged memory admission and serial
coordination. The coordinator observed 3,554,744 KiB below the 4,194,304 KiB floor;
no allocation is authorized by an older successful reading. If admission fails,
the owner-approved 2026-10-10 migration exception permits a source-reviewed
draft with local Release build, focused/full tests, formatting and package audit
marked NOT RUN, followed by exact-head hosted validation. It waives no check.

Protected merge requires zero-warning/error Release build, all four new cases,
the full 371-case suite, original diagnostics/exports, format/static/security
checks and unchanged coverage gates; fresh exact-main checks and raw artifact
readback follow merge. No deployment, database cutover, financial routing,
empty-string Country write change or broad source-entry closure is included.
