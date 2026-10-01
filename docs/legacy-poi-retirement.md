# Legacy public POI retirement (Epic 19.8)

Current managed POIs come from active supported `Places`; approved `TrashBins`
remain a separate system. No Place records are created, matched, rewritten or
deactivated by this change.

## Retired discovery paths

- Home no longer injects the 24 catalog parks, three static cafés or three static
  water points. Their marker builder, filters and check-in/reward sheet are gone.
- Explore and Nearby use the same managed Place payload as current markers,
  plus existing bins. A single Places map toggle replaces legacy facility toggles;
  Explore retains park and other-category filtering. Links/focus restore the
  current Place layer when necessary. Category artwork and Place popups are unchanged.
- Planner no longer has its independent 25-entry catalog (10 parks, five cafés,
  five shops, five water points). Regional/fallback planning reads nearby public
  eligible DogPark, DogFriendlyCafe and PetShop Places. Empty areas still generate
  the existing approximate loop, subject to the unchanged server walking-route step.
- Current-location OSM planning remains geographic route context. Generic parks
  are explicitly labelled “Zelena površina (OSM)” and are not verified dedicated
  dog parks. OSM shop/café stops identify their source and require local-rule checks.
  Water stops are omitted even for older requests with `includeWater=true`:
  fountains are not potable-water evidence. WaterPoint is future separate work.
- Community heatmap no longer republishes legacy visit coordinates as popular
  parks. Its compatibility `PopularParks` value is zero; the obsolete Home count
  is removed. Other social/activity aggregation and privacy rules are unchanged.
- Notification inbox/API no longer generate park recommendations from visit
  snapshots. Existing dated notifications remain history and are not rewritten.

## Historical compatibility

`ParkLocationCatalog` retains all 24 entries, keys and `Find` behavior strictly as
historical/reference identity. Existing `DogParkVisits`, saved plan/route snapshots,
labels, stamps, XP events and achievements remain untouched. Existing history,
statistics and achievement reconstruction continue using their established rules.
Unknown keys do not qualify for catalog-based achievements; stored labels still
render through the existing stamp/history path. There is no automatic matching by
name, distance or coordinates to Place IDs.

The only legacy visit-writing endpoint, authenticated antiforgery-protected
`POST /Map/ParkVisit`, now returns **410 Gone** without reading/writing progression
or visits. Ordinary Home has no check-in control or endpoint call. This does not
introduce a replacement Place check-in or new reward system.

## External owner reconciliation

No Admin CRUD/reconciliation tool or schema is needed. Keep any future decisions
outside runtime in an owner-reviewed ledger with these columns:

`LegacyKey, LegacyName, LegacyLat, LegacyLon, ReplacementPlaceId, Decision, Evidence, DecisionDate`

A proposed replacement requires independent identity/access/source verification.
Blank replacement IDs are valid. Never infer identity from the nearest Place or
a similar name. This ledger does not change visits, awards or runtime routing.

## Verification and boundaries

Permanent tests cover retired HTTP check-ins, current/empty Home payloads,
Explore/search/filter/Nearby/focus behavior, notification retirement, current
Planner source eligibility, generic OSM/water semantics, and historical known/
unknown-key stamps, idempotent achievement reconstruction and private heatmap
exclusion. Existing Place, bin, privacy, walking-route and GPS suites remain required.

Offline browser checks use synthetic HTTP-captured Home HTML at 320, 375, 390,
430, 1024 and 1440px. Run `DoggyDrop.Tests/Browser/legacy-poi-retirement.cjs` with
`DOGGYDROP_POPUP_CAPTURE`, `LEAFLET_TEST_ASSETS` and `LEGACY_POI_RESULTS` set to
external directories (same local assets as `map-attribution.cjs`). All requests
are fulfilled locally or aborted. No production/provider access is needed.

No schema, migration, snapshot, artwork, basemap, ORS profile/provider, GPS
recording, AddPoint, distance, proximity, Finish, route-history or legal-copy
change is included. Physical mobile browser/touch and real owner data checks remain
release follow-ups; this task does not authorize production access or deployment.

Local verification on 1 October 2026: build and explicit Razor rebuild PASS;
1,176/1,176 .NET tests, 141/141 Node tests, 48 Home/Explore layout cases plus
12 Nearby DOM cases, 198 icon/public-surface/popup/logo browser cases,
96 existing map-layout cases and 264 gallery marker renders PASS. All 26
standalone/Home/Active/Planner JavaScript syntax checks and Git whitespace checks
pass. Results and synthetic captures are outside the repository at
`C:/Codex/epic198/`. Existing NU1902 MailKit/MimeKit warnings are unrelated to
this change. No BLOCKER/HIGH/MEDIUM/LOW issue was found in the retirement change.
