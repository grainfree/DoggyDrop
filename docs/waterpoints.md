# WaterPoint infrastructure (Epic 20.2)

WaterPoint is drinking-water infrastructure, separate from TrashBin, Place,
DogBeach and retired hard-coded W markers. No legacy locations are migrated.
The baseline is `9d84f55bcdd068a0cb43fbaf7d9517208b305e5b`.

## Domain and lifecycle

Fields: Id, optional Name (200 characters), WGS84 Latitude/Longitude,
IsApproved, IsRetired, nullable DataSourceId, DateAdded, ApprovedAt, UpdatedAt,
Potability, Access, Seasonality and DogAccess. No owner, photo or reliability score.
Unnamed records remain unnamed and display as **Pitnik**.

Pending means unapproved and not retired. Retired takes precedence over approval.
Public eligibility requires approved, not retired, SourceReportedDrinking and
Unknown/Public/Permissive access. Restricted includes private, customers-only and
indoor facilities. Explicitly uncertain/non-potable records cannot be approved.
Database checks enforce coordinate/evidence ranges and the approval contract.
Explicit numeric zero follows existing WGS84 policy; omitted coordinates fail.

Retirement retains the row, source and timestamps. Reactivation restores the same
ID and only restores public visibility if otherwise approved. Approval timestamp
records the first curation approval, not a physical inspection. UpdatedAt is an
optimistic concurrency token at PostgreSQL microsecond precision; browser forms
round-trip it using ISO `O` format. No normal hard-delete endpoint exists.

Water quality, current operation and year-round availability are not guaranteed.
Seasonality defaults to Unknown; source-reported year-round is explicitly qualified.
DogAccess=Allowed preserves dog=yes, not proof of a dog bowl or dog-only station.
NotAllowed is displayed explicitly. Public source metadata is an allowlist of name
and safe HTTP(S) URL; contact fields, notes and concurrency metadata never leave it.
Source deletion uses SET NULL, consistent with existing provenance infrastructure.

## Admin and HTTP surface

`AdminWaterPoints`: GET Index/Create/Edit; POST Create/Edit/Retire. Retire's
`retired=false` reactivates. Index supports name, status and DataSource filters,
with bounded 100-row pages. Create/Edit reuse the Leaflet precision picker without
clustering; keyboard coordinate inputs remain available. All Admin actions require
the Admin role; every POST requires antiforgery. Stale forms return 409.

`AdminWaterImport`: GET Index/Map/Preview/Confirm/Result; POST Upload/Map/Select/
Prepare/Apply/Refresh/Cancel. Same Admin/antiforgery protection, upload bounds,
owner-bound in-memory sessions, expiry, confirmation versions and replay protection
as the existing importers. Single-instance deployment is required for those sessions;
restart loses only the preview, never a partially committed batch.

Public GET `/api/waterpoints` and `/api/waterpoints/{id}` return current safe DTOs.
POST `/api/waterpoints/{id}/route` is public with antiforgery, JSON-only model
binding, a 4 KiB body limit and the existing walking-route rate limiter. It accepts
only an explicit complete Origin; destination is loaded from current eligible
WaterPoint coordinates. Missing/retired/pending targets return 410 before the
shared IWalkingRoutes budget/provider. No new ORS client, key, profile or budget.
Responses are non-cacheable. Admin operations are also non-cacheable.

## Import contract and duplicate evidence

UTF-8 CSV parser is shared with Bin import: delimiter/header mapping, 5 MiB,
10,000 rows, 64 columns, 200,000 cells, 4,096 characters per cell. Required mapped
columns: Latitude, Longitude. Optional: Name, Access, Seasonality, DogAccess,
Potability. Enum names (case-insensitive) are the contract, not arbitrary numeric
values or raw OSM aliases. Missing evidence defaults to Unknown except Potability:
without that column, the explicit final confirmation declares that the source
identifies drinking-water infrastructure. Contradictory mapped evidence is rejected.
Final confirmation also excludes restricted/indoor and non-potable facilities.

Actual prepared batch headers:
`Latitude,Longitude,Name,Access,Seasonality,DogAccess,Potability`.
Existing DataSource is required. Preview writes zero WaterPoints. All final values
come from protected server state; an atomic transaction inserts approved records
with no user ownership, rewards or notifications. Source date is not rewritten.

Inclusive **15 metres**, geographic only, blocks potential duplicates regardless
of name/source/status, including retired records. Earlier valid upload rows also
participate even if flagged. The 1,365-record audit found 4/11/12/16 pairs at
10/15/20/25 m. Fifteen captures seven additional suspicious pairs beyond ten;
twenty adds only one while twenty-five may conflate separate taps. This is a
conservative review guard, not proof of identical hardware. All 16 source groups
within 25 m remain outside Batch 1 pending review. Two polygon records are over
1.4 km from any drinking-water node and remain outside the first batch for tap
position review. No known physical ground truth is invented for close pairs.

Final duplicate revalidation and insert share transaction advisory lock
`(194721,202)`. Manual Admin create/edit/lifecycle uses the same lock. UpdatedAt
also prevents stale form writes. Detection uses a bounded geographic query and 3D
spherical grid; >50,000 candidates or >1,000,000 comparisons fails closed. No
force-import bypass. Database failure rolls the entire batch back.

## Home and scope decisions

Home explicitly projects current WaterPoints and uses the existing combined
Leaflet.markercluster logical layers. Blue drop SVG markers distinguish water;
the icon is trusted static markup while all source text uses DOM textContent.
Pitniki toggles membership/counts without altering the source data. Refresh replaces
the logical layer. Bins, Places, logos, Featured and user/social/route separation
remain unchanged. Ordinary water artwork uses the existing 85%/95%/100% zoom tiers.

Water actions are infrastructure controls in Seznam, separate from Place cards.
Najbližji pitnik refreshes current eligible data, obtains location only on explicit
request and selects the nearest by spherical direct distance within 25 km,
independent of visibility filters. It opens the actual marker and labels the direct
distance; it does not claim to find the shortest walking path. No per-point routing.
Directions use the existing Home walking-navigation lifecycle and ORS foot-walking.
The target is revalidated by ID on every route request, never a cluster centroid.
410 terminates navigation; provider outage retains the existing clearly approximate
direct-line state without an invented walking ETA. Stale/cancelled requests cannot
replace newer state. No location is persisted by these new actions.

Both explicit water entry paths use setHomeUserLocation to create or reuse the
existing Home user icon directly on the map's userMarkers pane. It never joins a
location cluster. Later navigation GPS callbacks update that same instance; the
recording callbacks and Bin/Place navigation semantics are unchanged. Browser
regressions start from a bin deep link with no initial user marker and cover direct
water, nearest water, Bin and Place navigation followed by repeated water selection.

Home during an active walk can display this infrastructure layer through the same
Home controls. The dedicated Active Walk map is deliberately unchanged: adding a
second layer there would need a separate cockpit/clutter review. Planner and Saved
Plans do not yet accept WaterPoint stops. The independent ID/current-eligibility
service supports a future integration; it must revalidate IDs at new guidance start.
No partial saved-stop mapping, legacy water stops or historical changes are added.
WaterPoint URL deep links are deferred; popup/nearest selection uses existing reveal.

## Future contributions

Use Epic 20.1's evidence-versus-canonical-record pattern: pending proposals for
missing, location, non-potable, access, seasonality, duplicate or photo; Admin review
under the WaterPoint lock and token; preserve original DataSource and timestamps.
Do not reuse BinContribution.BinId for another entity or replace it with an unchecked
polymorphic ID. A future typed WaterPoint evidence relationship can reuse review,
photo-reference and privacy services. No second report UI or photo pipeline now.

## Source review and licensing

Research outputs remain outside the repository under
`C:/Codex/slovenia-community-poi-20260929/`: waterpoints_deep_review.csv/.md,
doggydrop_waterpoints_batch1.csv, waterpoints_provenance.csv. Snapshot:
2026-09-28 20:23:05 UTC. A/B/C/D = 1,272/70/2/21. Batch = 1,272 source-backed A rows;
not physically verified or imported. Unknown access/seasonality stay unknown;
private/indoor, non-potable and unresolved close records are excluded.
Generic fountains are not promoted just because they contain water.

The offline evidence gate is repository-owned at tools/waterpoint-source-evidence.cjs
and is exercised by the ordinary Node suite. The external review generator invokes
that exact module. Explicit negative potability, restricted access and unresolved
duplicates prevent A. fountain=decorative requires review even when the baseline
amenity is drinking_water; generic fountain alone is not positive evidence. Unknown
access alone remains eligible. Context review may demote further, never promote a
rejection. Node 13061729941 is B because its decorative subtype contradicts its
drinking-water amenity. It remains in the full review but is excluded from Batch 1
and selected provenance. No other classification changed in the strict-review repair.

© OpenStreetMap contributors; source-derived data is subject to
[ODbL](https://opendatacommons.org/licenses/odbl/1-0/) and
[OSM attribution requirements](https://www.openstreetmap.org/copyright).
Admin review, source labels or community corrections are not blanket license
compliance. Owner assessment of public-use, distribution and share-alike duties is
still required. No Terms/Privacy legal text is rewritten.

## Reproducible checks

Normal suite: dotnet build/test and `node --test DoggyDrop.Tests/JavaScript/*.test.cjs`.
Set DOGGYDROP_WATER_BATCH to the owner CSV to exercise the actual parser, mapping
and empty-database duplicate classifier without inserting records. Set
DOGGYDROP_WATER_CAPTURE and DOGGYDROP_POPUP_CAPTURE to an external directory for
HTTP capture. Browser fixture: `DoggyDrop.Tests/Browser/waterpoints.cjs`, with
LEAFLET_TEST_ASSETS, DOGGYDROP_POPUP_CAPTURE and WATER_RESULTS. All requests are
fulfilled offline or aborted. Covers seven widths, CARTO/OSM, DPR 1/2, popup,
nearest, directions, Admin pages, all eight filter states and up to 3,100 combined
locations. Measures performance without timing-based pass/fail assertions.

`WaterPointPostgresChecks.RunIsolatedAsync()` is explicit opt-in, fixed loopback
port 59218 with a UUID test database and finally cleanup. It migrates to the actual
pre-Epic migration, populates users/sources/bins/Places/dogs/walks/points/contributions,
then applies AddWaterPoints and compares all existing row JSON. Also exercises
checks, indexes, source FK, lifecycle, optimistic concurrency and overlapping imports.
Never point it at production. This Epic remains uncommitted for review.

Final local verification is recorded with the repair results outside the repository.
The isolated PostgreSQL checks cover migration over populated pre-Epic tables and
concurrent import handling.
WaterPoint browser fixtures cover 168 layouts, 32 combined filter states and four
synthetic datasets up to 3,100 locations, plus four user-marker lifecycle paths.
The actual importer validates the final owner-review CSV: 1,272 parsed, 1,272 valid,
zero invalid, zero internal duplicate conflicts and zero inserted rows. These
offline checks do not establish current physical water quality or availability.

Public routing HTTP regressions use anonymous and ordinary-user GET Home responses,
the real rendered requestVerificationToken and the same cookie jar. Missing/invalid
tokens return 400 without a provider attempt; a valid token reaches a mocked router.
No live ORS request or authorization/antiforgery configuration change is involved.
