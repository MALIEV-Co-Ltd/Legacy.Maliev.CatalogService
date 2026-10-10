# Material association creation source parity

Base: `5bc3a4a3ac3460a3fda5290fdf836d7b8c42f914`.
Private source anchor: `5fac706a7983a6d359b39acbd670e6800afe020e`.

The original ColorsController (`Maliev.MaterialService.Api/Controllers/ColorsController.cs`, blob `7a6a7b3c7b81f035cdf5a5f3b11bbd3c8887e7b2`) and SurfaceFinishesController (`Maliev.MaterialService.Api/Controllers/SurfaceFinishesController.cs`, blob `45e654f3089d0fba80657c9103462cca31ae7ae6`) use Any for creation existence checks. An existing pair returns 204 even when multiple stored links match. Read and delete separately use SingleOrDefault and retain their cardinality failure.

Catalog adds two repository AnyAsync queries and uses them only in association creation. Routes, DTOs, permissions, parent checks, read/delete lookups, schema and migrations remain unchanged. No private source implementation or configuration is copied.

Six HTTP/PostgreSQL cases cover both association families: duplicate-pair creation returns empty 204 without graph changes; duplicate-pair read/delete retain 500 without mutation; unique creation/read/duplicate/delete preserves the unrelated sentinel and verifies 401, 403 and missing-parent 404. The existing MaterialCollectionFailureFixture is unchanged. Full-suite forecast increases from 375 to 381; original diagnostic exports and coverage thresholds remain unchanged.

Local Release build, focused/full tests, format, package audit and native runtime checks: NOT RUN. The Document SDK lease has been released. A fresh local observation reports 6,772,652 KiB free physical memory, above the 4,194,304 KiB admission floor; memory alone does not grant SDK allocation. This task has no qualified local SDK custody manager or allocation grant and starts no SDK, testhost or container. The owner-approved 2026-10-10 migration exception permits reviewed draft publication before local validation. Hosted exact-head validation remains required before protected merge, followed by fresh exact-main checks and original artifact readback.

This bounded slice does not close overall source parity, negative pagination, country empty-write behavior, or downstream consumer migration. No deployment or database cutover is authorized.
