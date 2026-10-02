# Smart Walk Planner — Epic 21.0

Baseline: `c9ea926c629fe1dcf43c4442ecf60f076dce6dc2` (clean main).
No model, snapshot or migration change. No live provider experiments.

## Audit and architecture

Previously, Planner GET generated immediately. Location planning used
OsmWalkPlannerService/Overpass; regional planning used PlannerPlaceSource. Routing
used stops rather than generated fallback-loop vertices, so no-infrastructure
plans could collapse to start=end. Save/StartPlanned regenerated routes. Plan(id)
redirected to generation. Existing PlannedWalk already stores owned geometry and
coordinate/type stops; no schema extension is needed.

Default authenticated `/Walks/Planner` now renders Smart Planner without routing
or geolocation. `mode=manual` and parameterized bookmarks retain the advanced
flow, including its pre-existing Overpass/location-storage/approximate behavior.
SmartWalkController, SmartWalkPlanner/SmartWalkGeometry, SmartWalkPreviews and
SmartWalkEligibility form a layer above the existing IWalkingRoutes/OrsWalkingRoutes.
ORS remains fixed server-side `foot-walking` at the current HeiGIT endpoint, with
existing metrics, timeouts, cancellation, validation and shared budget. Keys are
neither inspected nor emitted. Home routing, recording, AddPoint, distance,
proximity, Finish, notifications and reward rules are unchanged. Saving awards the
existing CreateRoute event; starting calls the existing Start transaction/Walk flow.

## Product contract

- 15/30/45/60 minutes; loop default; optional explicit current public Place.
- Water/bin/DogPark preferences are soft bonuses, not guarantees.
- Start requires an explicit location-button or map action. Default map center
  is not a start. Keyboard users can pan and choose the map center.
- Named Place search supports the seven existing public categories. Only DogPark
  is an automatic preference; commercial categories need explicit selection.
  Featured/business status gives no ranking bonus.
- No LLM/personalization, weather, shade, elevation, off-leash or dog-legality claim.
- Displayed distance/duration come from ORS. Minutes are rounded up, labeled
  approximate. Departure from the preferred duration window and repetition >25%
  are disclosed. Access and water availability must be checked on the ground.

## Generation and resource limits

SmartWalkPolicy centralizes version `smart-v1`. Target speed is 75 m/min (4.5 km/h),
not a fabricated returned ETA. Preferred duration tolerance is ±20%; candidates
outside ±50% are rejected. A 30-minute request cannot become a 55-minute route.
At most three sequential candidates/provider attempts; explicit destination uses
one. No parallel provider fan-out or automatic retry loop.

Loop anchors: start, two intermediate points, start. Radius is target distance /
(2 + sqrt(3)) × 0.75; headings differ by 120°. Regeneration rotates by 137.507764°
per variant (0–31). Factor 0.75 leaves room for street-network detours. A later
candidate can substitute a preferred public POI within a 1.35 direct-detour factor.
These are provider inputs, NEVER approximate displayed/saved route geometry.

Provider round-trip was considered; waypoint routing keeps controllable DoggyDrop
stops and the existing provider-neutral abstraction. ORS describes length/points/
seed as approximate tour construction, not exact ETA ([provider explanation](https://ask.openrouteservice.org/t/description-of-round-trip-feature/4170)).
No extra-info/elevation is requested or inferred ([ORS documentation](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/extra-info/)).

- Authentication, antiforgery, JSON-only generation and 4 KB body limit.
- Existing 10 requests/IP/minute, no queue, on generation/search/SmartPlan.
- One generation per owner at once, three/owner/minute, eight globally.
- 26-second generation deadline, existing 8-second provider timeout.
- Existing shared provider limit 30/minute, 1,800/day, Retry-After backoff.
- Missing key fails before provider-budget use. Valid earlier candidate survives
  later busy/timeout. No route request from GPS callbacks.
- Explicit latitude AND longitude, finite WGS84 bounds, duration/mode/variant/ID/
  preference allowlist validation precedes generation lease/provider work.
  Explicit numeric zero retains existing coordinate policy.

No-store response allowlist: preview token, geometry, provider metrics, facts,
bounded named highlights, notice. No internal scores, private source data, identity
or credentials. Diagnostics remain in bounded in-memory selection/test state.

## Sources, facts and scoring

Three SQL projections use current approved/nonretired bins, PublicWater WaterPoints,
and ForPublicDetails DogParks with valid names. Explicit dog-prohibited WaterPoints
are excluded from Smart planning; unknown access is not labeled dog-friendly.
Water is source-reported infrastructure, not a quality or year-round guarantee.
No generic fountain/greenery becomes drinking water/DogPark.

Bounding queries handle longitude wrap and materialize <=120/type, ordered by
approximate proximity then ID. Spherical distance restricts results to half target
distance. No national entity load/per-vertex query. The bounded dataset is re-read
after provider work so intervening retirement is not described as a current fact.
Explicit stops are separately revalidated. This is not a census of every facility
along an unusually detouring route; coverage is deliberately bounded.

Actual returned geometry determines facts using inclusive shortest point-to-segment
distance: water/bins 25 m, park 40 m. Park allowance accommodates point/entrance
offset; proximity does not prove an accessible entrance/crossing. Local tangent
geometry handles antimeridian differences; a latitude prefilter preserves the
threshold. <=360 POIs ×20,000 vertices/candidate, no scoring simplification. Facts
are independent of selected preferences; preferences affect score, not claims.

Score starts at 100:

| Term | Rule |
|---|---|
| Duration error | −100 × absolute relative provider-duration error |
| Distance error | −15 × absolute relative provider-distance error |
| Repetition | −25 × repeated fraction for loops |
| Long detour | −30 × duration fraction beyond +20% |
| Requested water | +12 once |
| Requested bins | +3 each, capped at +6 |
| Requested DogPark | +16 once |

Earliest candidate wins ties. A 33-minute water route can beat a 30-minute route
without water; a 55-minute route for 30 minutes is invalid regardless of bonuses.
Repetition uses distance-weighted undirected 20 m cell edges resampled at <=10 m,
capped at 50,000 subsegments. Reverse traversals count. This heuristic can merge
narrow parallel paths; it is not a topological guarantee. Loop/out-and-back/long
stem with tiny loop/antimeridian regressions cover ordering. Repetition is penalized
and disclosed, not hidden when the best usable route still repeats.

Geometry gates: 2–20,000 valid vertices, positive finite provider metrics, valid
duration, geometric length 100 m to three times target; endpoints within 75 m,
loop closure within 75 m; explicit stops inside their corridor (75 m for explicit
destinations). Malformed/no-route failures get a retry message, never a generated
straight line or car route. Existing advanced approximate mode stays separate.

## Preview/save/start and history

Random 192-bit owner-bound preview token; one preview/owner; ten-minute TTL; 128
preview cap. Precise route data remains in process memory, not a persistent history
or cross-user route cache. Owner limiter has a 4,096-entry one-minute cap; a full limiter fails closed.
Expired limiter windows are removed under its lock before admission. Restart,
eviction or expiry asks for regeneration. Known deployment is one instance; future
multi-instance operation needs a deliberate shared-preview design. No production
configuration was inspected.

SmartPlan accepts token/dog/start intent only. It checks owner/dog/antiforgery,
serializes repeated saves with a preview semaphore and reuses the saved plan ID.
It rechecks preview expiry/replacement after acquiring the semaphore and after
database eligibility/lock waits before saving. Start uses the same existing recording
core, with an internal preview-validity guard immediately before adding the Walk.
Public saved-plan Start has no ephemeral-preview requirement. If a valid Save finishes
before expiry but the subsequent Start becomes stale, the saved plan remains and no
Walk is created. Final validity checks are the admission points for those operations;
expiry does not roll back an operation already validly admitted.
It saves exact selected ORS vertices/metrics and explicit stops in existing tables;
stop display names fit 120 characters without splitting a surrogate pair. Incidental
corridor facts are not reward stops and are not added to plan history. Save/Start
redirects do not carry preview facts forward as current claims: saved history omits
facts, and Active receives explicit stops only. Retirement of an incidental pitnik
therefore does not produce a stale “pitnik ob poti” claim after handoff; no route is
regenerated. The open preview remains a short-lived snapshot, not live availability.

Save/new Start revalidate eligibility under existing bin/water advisory locks and
PostgreSQL FOR SHARE Place row locks. Start retains existing owner/dog checks,
user lock and active-walk exclusion. Retired/pending/moved/deactivated explicit
stops refuse new guidance; same-location eligible reactivation permits reuse.
Saved schema has no infrastructure FK. Smart V1 records a stable, human-readable
public record reference in the existing Reason string: “Izbran postanek na predlagani
poti. Referenca #123”. The stop Type remains bin/water/park/place for the existing
Active/proximity/reward behavior. Only Smart validation parses this fixed prefix
and positive invariant ID, then checks ID + type + original coordinates + current
eligibility. Missing/malformed references fail closed. A replacement at the same
coordinates cannot stand in for a retired/moved/deleted original. Reactivating the
original at its original location permits reuse. Legacy saved plans are unaffected;
there is no schema/model change or new foreign key. Historical geometry
is never rewritten; Smart Plan(id) renders it without regeneration. Active already
renders planned route/stops separately from GPS; no new recording system is added.

## UI, privacy and limitations

Labeled native controls, cards/chips, readable facts with existing owned icons,
distinct start marker, no duplicate loop-end marker. At most five useful route POI
highlights, no national marker flood. Popup/fact names are text nodes. Shared
Leaflet 1.9.4 basemap/provider/retina/fallback/attribution remain. Route credits
stay visible/clickable/focusable below actions. Normal document flow and bottom
padding permit controls to scroll clear of fixed navigation and safe-area occupancy.

Generate enters readable busy state immediately. Revision + AbortController reject
double submit/stale success/stale failure/cancelled completion. Input changes clear
route/token. Layers are replaced without accumulation. Save/Start use native POST
redirects, preserve submitter intent and restore controls on pageshow. No location
in Smart URLs/localStorage and no automatic geolocation prompt. Existing advanced
Planner behavior remains distinct. ORS receives requested coordinates/anchors;
saving deliberately creates an existing owner-only PlannedWalk. No new analytics
or coordinate-bearing normal logs. Existing basemap/font flows remain.

Permanent suites: SmartWalkTests, SmartWalkHttpTests, explicit-loopback
SmartWalkPostgresChecks, JavaScript/smart-walk.test.cjs, Browser/smart-walk.cjs.
They cover eligibility, validation, caps/expiry, geometry/scoring, failures, ownership,
save/start/history, lifecycle, SQL translation and Place locking. Synthetic
250/500/1,500/3,000 infrastructure sets and 20,000 vertices check bounded work.
No fragile millisecond assertions.

Local Razor capture: DOGGYDROP_SMART_CAPTURE during .NET tests. Browser fixture:
DOGGYDROP_SMART_CAPTURE, LEAFLET_TEST_ASSETS, SMART_WALK_RESULTS and Playwright on
NODE_PATH. Every network request is intercepted. Widths 320/375/390/430/768/1024/1440,
OSM/CARTO, 125% text, landscape, safe area, saved routes, keyboard, failure/retry.
Results/screenshots remain outside repo. PostgreSQL uses a disposable UUID DB at
127.0.0.1:59218; no migrations/configuration or production connection.

Remaining owner trial: live urban/rural/sparse/barrier route quality and iPhone
Safari/PWA permissions/back navigation. Synthetic tests cannot certify live route
quality, dog access or actual ORS account quota. Verify existing server-side ORS key/
quota operationally without exposing it; no deployment/config change authorized.
Deferred: weather/heat/shade/surface data, dog-restriction routing, personalization,
learning, automatic rerouting, upcoming-POI alerts, community confirmations, LLM
planning and distributed preview cache. This is not Privacy/legal finalization.

## Repository scope

Four modified files: `Controllers/WalksController.cs`, `Program.cs`,
`ViewModels/WalkPlannerViewModel.cs`, and `docs/privacy-data-audit.md`.
Thirteen new files:

- `DoggyDrop/Controllers/SmartWalkController.cs`
- `DoggyDrop/Services/SmartWalkPlanner.cs`
- `DoggyDrop/Services/SmartWalkPreviews.cs`
- `DoggyDrop/Services/SmartWalkEligibility.cs`
- `DoggyDrop/Views/Walks/SmartPlanner.cshtml`
- `DoggyDrop/wwwroot/css/smart-walk.css`
- `DoggyDrop/wwwroot/js/smart-walk.js`
- `DoggyDrop.Tests/SmartWalkTests.cs`
- `DoggyDrop.Tests/SmartWalkHttpTests.cs`
- `DoggyDrop.Tests/SmartWalkPostgresChecks.cs`
- `DoggyDrop.Tests/JavaScript/smart-walk.test.cjs`
- `DoggyDrop.Tests/Browser/smart-walk.cjs`
- `docs/smart-walk-planner.md`

No external harness/screenshot/local configuration/build artifact belongs in this
scope. The complete specification through section 345 and its explicit END marker has
been received and reviewed. No specification tail remains outstanding.

## Continuation review and verification

The strict identity review found and fixed a stale-guidance bypass: an eligible
same-coordinate replacement previously satisfied a retired original's saved stop.
Regression cases now cover bins, water, parks and explicit Places, including moved
originals and reactivation. A bounded-cache owner-limiter admission edge case was
also fixed to fail closed; preview expiry is rechecked after waiting for Save.

HTTP tests cover expired/unknown/foreign-owner Save and Start, concurrent Save/Start,
client geometry/metric tampering, and retired incidental facts. Browser tests exercise
native double activation and keyboard submission of Save, Start and saved Start at
six widths. Native redirects remain; no AJAX save protocol was added.

Local measurements (synthetic data, no provider latency; medians of ten warmed runs):

| Operation | Fixture | Median ms |
|---|---|---:|
| Candidate generation | Three candidates, 120 nearby rows | 0.086 |
| Infrastructure query | SQLite, 3,000 water rows, returns 120 | 14.329 |
| Route scoring | 20,000 vertices | 22.230 |
| Route facts | 120 rows × 20,000 vertices | 157.702 |

These are diagnostic measurements on the local host, not production latency promises
or timing gates. Browser rendering is measured separately at 250/1,500/5,000/20,000
vertices. Geographic queries remain capped at 120 per type (360 total).

Provider accounting: all-success loop = 3 attempts; partial ordinary failure still
<=3; early busy/internal timeout can stop at 2 with an earlier valid route; total
ordinary failure = 3; explicit destination = 1; invalid request = 0. The unchanged
shared provider budget still applies to every real attempted candidate.


Final local verification for received sections 0–345: build passes (only unchanged
MailKit/MimeKit NU1902 warnings); 1,428/1,428 .NET, 227/227 Node. Smart-focused
.NET is 92: 30 unit/scoring, 17 service/eligibility, one measurement fixture, 44 HTTP.
Smart Node is 16. Isolated PostgreSQL is 8/8; its temporary server was stopped.
Explicit Razor compilation and 32 standalone/inline JS checks pass. Tracked diff
and all 17 intended UTF-8 files pass whitespace/scope checks.

Offline browser results: Smart 92 layouts, 60 lifecycle, 14 saved-history,
18 native submission, 14 expired/unavailable layouts, three hostile-name cases,
four geometry performance fixtures. Existing Home 196/16/4
(layout/lifecycle/performance); Water 168 layouts, 32 filters, four marker lifecycle,
four performance fixtures; attribution 96; navigation 144 + 36 dynamic/reset;
Places/icons 198 and gallery 264; contributions 108 submission + 108 mobile + 54
layout + four Home request cases. Existing unchanged-surface captures use their
local regression fixtures and current application assets. No external request is
allowed through those browser fixtures. Physical iPhone and live route quality
remain owner checks; no production/provider call was made.

Final remaining severity: BLOCKER 0, HIGH 0, MEDIUM 0; LOW 1 documented operational
limitation: process-local preview availability across restart/deploy/another instance
or eviction. It fails closed with localized regeneration guidance. This is not a
route-integrity exception. Unknown physical accessibility/route quality remains an
owner field-test requirement, not an automated safety certification.

The final continuation adds deterministic expiry/replacement during submissions,
mixed concurrent Save/Start, missing/invalid antiforgery across all browser mutation
endpoints, actual expired/unavailable Razor captures at seven widths, and hostile
Place/DogPark/WaterPoint result rendering. Screenshot artifacts are viewport captures
outside the repository under C:/Codex/epic210/browser-final (mobile-input.png,
mobile-loading.png, mobile-result.png, desktop-result.png, mobile-error.png,
mobile-expired.png, mobile-unavailable.png). All provider requests are fixtures.
