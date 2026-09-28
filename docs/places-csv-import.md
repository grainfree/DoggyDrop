# Places CSV import (Epic 19.1)

## Admin workflow

Open **Uvoz lokacij** in the shared data-administration navigation. Select an
existing DataSource, choose one fixed category (recommended for a batch of shops)
or category mapping, upload UTF-8 CSV, check the mapping, review/select rows,
confirm, and inspect the result. Upload, mapping and preview create no Places.
The result shows the source, total rows, imported rows, possible duplicates,
invalid rows and unselected ready rows, with links to Places, another import and
the Home map. Selection is paged at 100 rows; save it before changing pages.

All endpoints require the existing Admin role. All mutations are antiforgery
protected. There is no change to authentication configuration or public routes.

## Shared parser and mapping

The importer directly reuses Epic 19's `BinImportCsv` parser, limits,
`BinImportMapping.Coordinate`, shared result/status/source records and exception
type. The existing Bin importer implementation is unchanged. Place-specific
mapping, session state, duplicate classification and views remain separate.

Limits: 5 MiB, 10,000 records, 64 columns, 200,000 cells, 4,096 characters per
field. UTF-8/BOM, comma/semicolon, quoted and escaped fields and multiline text
use the same strict parser. Ambiguous delimiter detection asks the Admin to
choose. Malformed input fails safely; XLSX/GIS is unsupported.

Required mappings: Name, Latitude, Longitude and Category unless fixed. Optional:
Address, Phone, Website and Description. Different imported fields must map to
different valid columns. Category accepts the seven existing enum names or
their existing English category keys, case-insensitively. Unknown categories
are invalid. A fixed category overrides and ignores CSV Category.

A synthetic Mr.Pet-shaped fixture covers Name, Category, Address, Latitude,
Longitude, Phone, Website, CoordinateStatus, IsActive, Source and SourceUrl.
Select PetShop and map the six corresponding supported fields; the other
columns are ignored. Select the authoritative DataSource in Admin. There is no
brand-specific application code and no real store dataset in these tests.

Place validation reuses `PlaceInput`: trimmed nonempty Name <=120 characters,
Address <=180, Phone <=40, Website <=500 and safe HTTP(S), Description <=2,000,
supported category, finite latitude [-90,90] and longitude [-180,180]. Names must
also satisfy the existing public eligibility check against control characters.
Zero coordinates remain allowed by the current Place policy. The importer does
not geocode, swap coordinates, convert projections, silently truncate mapped
fields or evaluate formula-like text. Preview uses Razor-encoded plain text;
CSV websites do not become clickable preview links.

## Persistence, duplicate rules and limits

Only new Places are created. They are active, unfeatured with no Featured dates,
logo, image, amenities or verification metadata. CreatedAt/UpdatedAt use normal
UTC microsecond precision. There is no owner/contributor or importer public badge.
The selected existing DataSource ID is immutable in the server session and
revalidated inside the final transaction. Source metadata is never updated.

Duplicate identity directly uses Epic 18's `DuplicateCandidates.NormalizeName`,
`Distance` and `PlaceMetres`: same trimmed/whitespace-normalized, invariant-case
name AND distance <=75 metres, including inactive Places and other categories
or sources. Different names at the same coordinate are not automatically
duplicates. The same rule covers earlier valid CSV rows, existing Places and
reimports. Earlier possible-duplicate rows participate conservatively in chain
detection. Possible duplicates cannot be selected; no existing Place is merged,
reactivated, updated or overwritten. Up to three examples explain a duplicate.

Classification uses one geographically bounded database query and normalized
name plus Earth-centred spatial buckets (including poles/date line). More than
50,000 existing geographic candidates or 1,000,000 distance comparisons causes
a clear split-the-file error, never partial duplicate detection. No per-row
query/save occurs. The performance test checks 10,000-row preview classification
and a 1,000-row import with two SELECTs and one SaveChanges.

Final import takes PostgreSQL transaction advisory lock `(194721, 191)`, distinct
from Bin import. It revalidates source and selected rows against the current
database and one another, then AddRange/one SaveChanges/commit. Any newly found
duplicate aborts the entire batch and requires preview refresh. Any write failure
rolls back all selected rows and clears failed tracked entities. This serializes
overlapping Place imports; ordinary manual Admin edits do not acquire this lock.
It is not a universal database uniqueness constraint for all writers.

## Preview lifecycle and privacy

Random owner-bound IDs, fixed 30-minute expiry, per-session serialization,
versioned selection/confirmation and completed-result replay protection keep
the browser from supplying authoritative Place fields. Expired, cancelled,
foreign or missing sessions cannot import. Unused CSV columns are discarded
after mapping; successful imports drop row payloads and retain only the summary
until expiry/eviction. There is no raw CSV application logging or persistence.

Place sessions have a separate conservative 64 MiB reservation budget, at most
8 sessions, at most 2 unfinished sessions per Admin and 2 concurrent parser
operations. This reservation is not an exact process RSS limit: bounded request,
query and classification working memory is additional. Existing Bin sessions
have their own budget. Split files or finish/cancel sessions if capacity is full.

The current supported deployment is **one Render instance, no autoscaling**.
Preview state is instance-local and is intentionally lost on restart/deployment;
the Admin must upload again. Scale-out requires a shared session design before
enabling multiple instances. The PostgreSQL import lock alone does not make
in-memory preview state distributed.

The importer handles public business/infrastructure data. Source ContactName,
ContactEmail and Notes are absent from its source projection and from public
Place responses. GPS, accounts, notifications, deletion/export, Bin/photo
workflows, Amenities, Featured and SEO semantics are unchanged. Imported Places
use the existing Home map query, Discovery, Details/friendly URL, SEO/sitemap
and Saved Places without importer-specific public code.

## Verification and operational checks

Permanent `PlaceImportTests` and `PlaceImportHttpTests` cover parser reuse,
mapping, all categories, field/coordinate/URL limits, ownership/expiry/capacity,
actual Admin/antiforgery HTTP enforcement, source and final-field tampering,
read-only preview, pagination/stale selection, refresh/cancel, reimport, source
deletion, new duplicates, default fields, unchanged existing Places/source,
rollback, bounded query/save counts and public integration/privacy.

The six isolated PostgreSQL checks invoke the same test assertions for duplicate
rules, defaults/reimport, final revalidation, injected rollback, performance and
two overlapping imports. They use synthetic local databases with EnsureCreated;
no migrations or production connections. The concurrency assertion is retained
as `PlaceImportTests.CheckConcurrency` for the isolated PostgreSQL harness.

Layout fixtures render the actual five importer pages at 320, 375, 390, 430,
1024 and 1440 pixels. Local CSS, labels, table headers, keyboard checkbox use,
focus and reachable confirmation actions are checked; the preview table can
scroll horizontally. External layout/PG helpers and screenshots are not part of
the repository. Full .NET and Node suites cover existing Bin import, Places,
DataSource, Amenities, Saved Places, Featured, SEO and privacy regressions.

Verified on 2026-09-28: build/Razor passed; 971/971 .NET tests (45 new importer
cases), 82/82 Node tests, 6/6 isolated PostgreSQL checks, 30/30 layout fixtures,
12/12 standalone JS syntax checks and Home/Active inline JS checks passed.
Tracked diff and all new files passed whitespace/UTF-8 checks. Existing MailKit
and MimeKit dependency advisory warnings remain outside this Epic's scope.

Before a real import, the owner must verify CSV source permission, coordinate
accuracy and business details, select the correct DataSource/category, and
review the preview. Real phone/browser/screen-reader checks remain manual.
No actual Mr.Pet import, migration, production access, commit, push or deployment
is part of this implementation task.
