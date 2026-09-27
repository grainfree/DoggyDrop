# Municipal bin import (Epic 19)

Admin-only `AdminBinImport` supports upload → explicit column mapping → paginated preview/selection → confirmation → atomic insertion. An upload or mapping never inserts bins. Every POST uses antiforgery; state is bound to the authenticated Admin, expires after 30 minutes and is held only in bounded process memory. A restart or routing to another app instance requires a fresh upload. No schema migration is needed.

## Input and preview

- UTF-8 only, including BOM. Invalid UTF-8 is rejected with instructions to export UTF-8; Windows-1250/1252 is never guessed.
- CSV comma or semicolon. Auto-detection succeeds only when one interpretation is usable; otherwise Admin must explicitly select the delimiter. The bounded parser supports quoted delimiters, escaped quotes and embedded line breaks, and rejects malformed quoting/null bytes.
- Maximum 5 MiB, 10,000 data records, 64 columns, 200,000 cells and 4,096 characters per field. Row numbers count CSV records, including the header, not physical lines inside quoted cells.
- Admin explicitly maps distinct latitude/longitude columns and optional name/description and address columns. Header suggestions exclude x/y. Coordinates must be finite WGS84 values; no geocoding or projection transformation occurs. Decimal commas are accepted only within a single correctly parsed field; thousands separators and exponent syntax are unsupported.
- `TrashBin` currently has only a required, database-unbounded `Name`, not separate description/address fields. Import applies conservative limits of 200 characters for name/description and 180 for address, combines them as `Name · Address`, and previews the exact persisted value. Empty names become `Koš`. No existing model limits or semantics change.
- Unmapped columns are discarded after mapping. Files/rows are never logged or permanently stored. Two concurrent parsers, at most eight sessions globally/two unfinished sessions per Admin, and a conservative 64 MiB state reservation limit bound memory; expired sessions are pruned at least once per minute. Completed summaries retain no rows.
- Preview statuses are READY / POSSIBLE_DUPLICATE / INVALID. All READY records across the full file are initially selected. Preview renders 100 records per page; selection updates affect that page only. Save before navigating; the continue button saves the visible selection before showing confirmation. Invalid and possible-duplicate records cannot be selected in V1. There is no force-import override.

## Duplicate checks and transactions

The threshold is Epic 18's inclusive 20 metres. All persisted bins, including pending bins, are considered. Later valid file records are conservatively checked against earlier valid records. Preview shows up to three examples per row, with existing ID, source, status and exact-distance approximation, or the earlier CSV row number.

For a chain where A is within 20 m of B and B is within 20 m of C, but A and C are farther apart, A is READY (absent an existing-bin match), B is a possible duplicate of A, and C is a possible duplicate of B. Excluding B does not automatically promote C. Admin must review/split or correct the input; V1 does not force-import these conservative candidates.

A geographic bounding query fetches at most 50,001 records. More than 50,000 existing candidates aborts classification and asks Admin to split the input geographically. Earth-centred 3D buckets use the same Haversine rule as Epic 18, handle poles/date-line adjacency, and cap exact comparisons at 1,000,000. No partial duplicate scan is presented as complete. There is one bounded DB duplicate query per classification, no per-row lookup.

Final insertion revalidates the source and selected rows against current persisted bins and each other inside a transaction. PostgreSQL `pg_advisory_xact_lock(194721, 19)` serializes all importer instances through revalidation and commit. `AddRange` and one `SaveChanges` insert the batch atomically. New conflicts abort the batch and require refreshed review; failed saves expose no DB error details. Replaying a completed session never inserts again; a fresh upload of the same file detects the newly persisted records.

**Remaining race:** ordinary community/Admin Map writers do not acquire the importer advisory lock. A Map insertion after the final duplicate read can still race an import. There is no spatial unique constraint. The importer deliberately does not change existing Map/approval code to close this race. Ordinary writes committed before revalidation are detected.

## Approved-bin semantics

Imported bins have `IsApproved = true`, `ApprovedAt = DateAdded = ingestion UTC time`, selected `DataSourceId`, and `UserId = null`. These timestamps do not claim an installation date. Source `DataDate` and metadata remain unchanged. Import has no reward/achievement/notification service dependency and does not call Nearby Discovery fan-out. Existing bin data is never updated, moved, merged, deactivated or reassigned.

The result shows the source, total parsed count, imported count, possible duplicates excluded, invalid rows and unselected READY rows. These categories sum to the total. Import-session IDs, filenames, unused columns and validation reasons never become bin fields or public DTO properties.

## Verification

`BinImportTests` covers CSV, mapping, coordinates (including the existing valid zero-coordinate policy), bounds, expiry, geographic duplicate rules/chains, structural comparison counts, query bounds, one save/commit per 1,000-row batch, source deletion, stale previews, reimport, rollback without modifying existing bins/sources, approval/provenance, Epic 18 filters/counts/duplicate review/SET NULL, and suppression of notifications/rewards (including an opted-in nearby user). `BinImportHttpTests` covers Admin/antiforgery, upload/map/preview/confirmation, full summary counts, pagination selection, source/coordinate overposting, cross-Admin sessions, expired/stale confirmations, replay, selected-only insertion and the actual public bin API without import metadata. The local isolated PostgreSQL harness also exercises transaction rollback and overlapping import transactions. No application startup migrations or production connections are used in these checks.
