# Filtered Material group wire shape

The machinable and printable list endpoints now query the repository without
loading `MaterialGroup`. Their existing scalar `MaterialGroupId` remains, while
the unloaded nullable group field is omitted from the response. Paginated
Material results still include the related group.

This restores the two filtered-list methods together from private source commit
`5fac706a7983a6d359b39acbd670e6800afe020e`. The original controller filters the
Material set directly without `Include`; its pagination query explicitly
includes the group. The original model's group navigation is unloaded, the
service registers SQL Server without lazy-loading proxies, and Startup omits
null JSON fields. No private source text, history or configuration is copied.

| Original source path | Git blob |
| --- | --- |
| `Maliev.MaterialService.Api/Controllers/MaterialsController.cs` | `bc7b3712c109e437ba704a0ab5b31edbfd24ef45` |
| `Maliev.MaterialService.Data/Database/MaterialContext/Material.cs` | `cc5f9cb965465e593d51634964b9f59b75a91625` |
| `Maliev.MaterialService.Api/Startup.cs` | `a0314f7ed6ff7d5a778f98d636d37c04b66e8bdf` |
| `Maliev.MaterialService.Api/ServiceExtensions.cs` | `aee7e0509ec40ad46a6a8b09f14eb0114590dc07` |

The implementation reuses the already prepared filtered-list candidate published
as source intake through PR #73, adapted against accepted protected main
`7f699f7c5992d0cfe045b8dbdd843f21e6105ed4`. Only the two list calls change.
DTOs, controllers, repository methods, cache policies, pagination, zero-size
behavior, default/negative bounds, Country writes and associations are unchanged.

Four normal HTTP/PostgreSQL cases reuse the current
`MaterialCollectionFailureFixture` byte-for-byte. Test consumers adapt its
existing contexts and scoped signed clients; no new fixture or resource framework
is introduced. Both route variants assert group omission, retained group ID,
filter membership, scalar names/price/timestamps, detail-group omission, and
paginated group inclusion. Both also assert 401/403 and empty-list 404 without
changing stored rows or groups. The full inventory forecast increases from 371
to 375; it is not an executed result. All original Country/health diagnostics,
exports, coverage thresholds and workflow requirements remain intact.

Local SDK execution requires immediate memory admission at the unchanged
4,194,304 KiB floor, serial coordination and verified resource custody. Memory
observations vary, and an older successful observation grants no allocation.
No local SDK or database worker is launched by this source packet. When resource
admission blocks validation, the owner-approved 2026-10-10 migration timing
exception permits a reviewed draft with actual unrun checks and the blocker
recorded; it does not waive any check.

Protected merge requires exact-head Release build with zero warnings/errors,
all four HTTP cases and the full 375-case suite, original diagnostic exports,
format/static/security/contracts and unchanged coverage gates. Fresh exact-main
checks and original-artifact readback follow merge. This slice does not accept
the whole source entry, authorize deployment or database cutover, or close
other Material and joined consumer obligations.
