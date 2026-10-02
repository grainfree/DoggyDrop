# Community infrastructure confirmations (Epic 22.0)

Baseline: `7a71e661c2eb91281f834bd9e8a1c4f36e2e2a2a` (`Add Smart Walk Planner`).

## Audit and decisions

The former public bin reliability display starts at 60, adds `min(18, UsefulVotes*3)` and `min(15, UsedCount)`, subtracts `min(28, FullReports*8)` and `min(35, MissingReports*12)`, and clamps to 0–100. These lifetime counters have no independent-author or physical-verification semantics. Home's numeric display is replaced with community evidence. Counters, diagnostic APIs, nearest/best-bin selection and legacy planner logic are retained. Old saved-plan explanation text is historical, not retroactively rewritten. Smart Walk scoring is unchanged.

Existing bin issue/photo/location contributions have an Admin review lifecycle; positive observations do not. They are not inserted into BinContributions or its Pending/Approved/Rejected history. “Moji prispevki” remains unchanged. WaterPoint negative reporting is deliberately deferred (specification option A): extending typed bin/photo/review/storage semantics would be a separate feature. There is no second reporting system. Existing bin reports remain available without requiring proximity.

No XP, rewards, notifications, automatic walk-context confirmation, or post-walk prompts are introduced. Existing walk proximity alerts use 120 m (85 m for planned stops) and at most 50 m reported accuracy. Those alerts, GPS recording, AddPoint, distances and Finish are unchanged and are not evidence for this feature.

## Domain and privacy

`InfrastructureConfirmation` is an accepted immutable observation:

- `TrashBinPresent`: the bin was observed present, not necessarily empty, clean, undamaged, or correctly described.
- `WaterPointWorking`: water was observed working. This does not verify laboratory potability, permanent access, year-round availability, or safety for dogs.

The table stores ID, type, exactly one typed infrastructure FK, nullable author FK, server UTC timestamp and the infrastructure's evidence version. A database check ties the type to exactly the correct non-null FK. There is no unconstrained type/ID pair. Future Place support requires its own FK/type/check and explicit semantic decision.

**No user confirmation latitude, longitude, accuracy, distance, IP, raw request, WalkId, or trail is persisted.** An accepted event implies the current proximity rule passed; no redundant proof flag is needed. Infrastructure coordinates remain ordinary public infrastructure data. The event still links an authenticated person to an infrastructure observation and time, so it is personal activity data while the author FK exists.

Account deletion sets the author FK to null, following existing retained contribution-history conventions. Anonymous historical observations remain in last-observed/history results but contribute zero to distinct-user counts. No replacement identifier or identity hash is stored. Exceptional infrastructure hard deletion is restricted when evidence exists, preserving FK integrity and history; normal retirement remains available. No automatic age-based deletion is introduced. General retention/legal-basis decisions remain owner/legal work, not resolved by this implementation.

## Acceptance, cooldown and concurrency

Authenticated `POST /api/confirmations/bin/{id}` and `/api/confirmations/water/{id}` require the existing antiforgery token, JSON content type and a body of at most 2,048 bytes. Only nullable `latitude`, `longitude`, `accuracy` input members are accepted as evidence. Missing/non-finite/out-of-WGS84 inputs and accuracy outside 0–10,000 m are rejected. Explicit numeric zero is a valid coordinate, not a substitute for omission. The server supplies author, target type, timestamp and summary.

The current eligible target is loaded inside a transaction. The server uses the existing `DuplicateCandidates.Distance` Haversine helper. Acceptance requires **accuracy ≤25 m AND distance + accuracy ≤50 m** (inclusive). A center outside 50 m is too far; a poor/ambiguous fix is insufficient accuracy. This conservative 50 m envelope allows modest infrastructure/source and phone uncertainty without reusing the much wider walk-alert radius. It is a product guard, not a statistically proven attendance test. [W3C Geolocation](https://www.w3.org/TR/geolocation/#accuracy) defines accuracy as a radius; the specification also describes 95% confidence and explicitly makes no guarantee of the actual device location. Physical iPhone calibration remains necessary.

One accepted event per authenticated user and object per **24 hours**, including across lifecycle resets. At the exact 24-hour boundary a new observation can be accepted with a new proximity check. Other users can contribute independently. Server UTC comes from injected TimeProvider.

Existing bin/water transaction advisory locks plus PostgreSQL `FOR UPDATE` target-row locks protect current coordinates/eligibility, cooldown read and insert. The advisory lock serializes simultaneous confirmation requests, including multiple tabs; row locking also excludes non-advisory infrastructure updates during acceptance. No SELECT-then-INSERT race outside a transaction. No infrastructure mutation, counter/reward write or ownership transfer is performed by confirmation.

ASP.NET's existing rate-limiter infrastructure provides **20 attempts per authenticated account per minute, no queue**, separately from routing and contribution-upload budgets. This process-local limit resets on process restart and is per instance; the DB cooldown remains shared and authoritative. Browser busy state is additional UX protection. Location spoofing and multiple-account abuse remain possible; this is community evidence, never an official verification badge.

## Evidence periods and issue priority

Neither bin approval timestamps nor WaterPoint's generic UpdatedAt can express reactivation safely. Each infrastructure record therefore has one confirmation-specific `EvidenceVersion` UUID. Existing rows start at the empty UUID without altering any existing data. Tracked changes to coordinates, approval or retirement create a new version; bin rejection and water potability/access changes do too. Both SaveChanges paths share this logic. The existing bulk approval's ExecuteUpdate explicitly sets a new version. Other audited ExecuteUpdate paths change only photos/ownership/counters, not physical eligibility. New raw physical/lifecycle writers must set a fresh version too.

Retire/reactivate at identical coordinates cannot revive old evidence. Ordinary name, photo, source, seasonality and counter edits do not reset it. Old confirmation rows are retained unchanged. Cooldown intentionally spans versions. EvidenceVersion is not a public DTO field.

Bin issue priority includes any pending issue and the existing “full/missing” counter thresholds (≥2), independently of positive observations. Approved wrong-location reports applied their correction and approved missing reports applied retirement; those reviewed records are historical. Approval of damage/inaccessible/duplicate/other reports does **not** prove repair or resolution, so these remain conservatively flagged. No report text or reporter identity is public. Because the existing workflow has no explicit repair/resolution status, those latter historical flags cannot be cleared by a positive observation; a future resolution workflow is separate work. Legacy report counters likewise retain their existing lifetime semantics. Admin notes are never interpreted as machine-readable repair evidence.

Public priority is `issue`, otherwise `recent` (≤30 days), `older` (>30–180 days), `stale` (>180 days), or `unconfirmed`. Thirty days gives a useful current community window; 180 days separates older infrastructure evidence without declaring it invalid. Same windows for bins/water avoid unsupported distinctions. Unique counts consider distinct non-null authors in the last 30 days, current evidence version and non-future timestamps. Last observation uses the latest event in that evidence period. Calendar-relative Slovenian text uses Europe/Ljubljana; older observations show an exact local date, avoiding invented month precision.

Unconfirmed/stale does not hide infrastructure or change eligibility, routing, nearest selection, or Smart Walk. Source provenance is displayed alongside evidence, and no observation updates DataSource, ownership, potability, access, or seasonality.

## Public and Admin UI

Home bin detail and WaterPoint popup have compact evidence and secondary “Koš je še tukaj” / “Pitnik deluje” actions. Anonymous visitors see a login link before any confirmation location request. Geolocation uses explicit one-shot `getCurrentPosition`, high accuracy, no cached position, 12-second location timeout; no watcher is added. A 30-second overall deadline releases UI even if permission/network stalls. Permission denial, poor accuracy, distance, rate limits and failures return readable retry feedback. A retry after an uncertain response is protected by the DB cooldown.

One delegated click listener handles native button clicks/keyboard activation. Busy/disabled/aria-busy states begin immediately. Popup close, new target and pagehide cancel the intent; late callbacks cannot act on another target. Only server response summaries update public evidence. No animation is required to understand loading. Water popups have a scrollable height limit and safe top/bottom autopan padding. Existing navigation/attribution and cluster membership remain intact.

Admin-only, no-store `/AdminConfirmations?kind=bin|water&id=...` shows current public summary and paginated (100/page) lifetime event timestamps with current/prior evidence periods. No author identity or exact location is exposed, no moderation/delete action is added. Links are available from Admin bin list and WaterPoint editor.

## Queries, migration and verification

Home uses three aggregate queries total: bins GROUP BY/MAX/COUNT DISTINCT, issue IDs, water GROUP BY/MAX/COUNT DISTINCT. No lifetime event arrays are loaded. Each table has target/version/time and author/target/time indexes for actual summary/history/cooldown queries. No new cache or external service is introduced; accepted responses show the authoritative transactional summary immediately.

Migration `20261002195147_AddInfrastructureConfirmations` adds only the table, typed FK/check/indexes and the two infrastructure evidence-version columns. Down removes only those additions. The isolated PostgreSQL fixture migrates a populated pre-Epic database with users, sources, bins, water, Places, contributions, walks/points and saved plans, compares all prior values, and also checks rollback. Production migration is not executed.

Permanent tests: InfrastructureConfirmationTests, InfrastructureConfirmationHttpTests, InfrastructureConfirmationPostgresChecks, JavaScript/infrastructure-confirmations.test.cjs and Browser/infrastructure-confirmations.cjs. The PostgreSQL runner is opt-in at the existing fixed loopback test cluster; it creates and removes its own UUID database, never reads application credentials. Performance fixtures use 250/500/1,500/3,000 infrastructure and 10,000/50,000 history rows, assert query/aggregate counts without timing pass/fail thresholds.

Browser fixture requires `NODE_PATH` with Playwright, `LEAFLET_TEST_ASSETS`, `DOGGYDROP_POPUP_CAPTURE` with current rendered Home captures and `CONFIRMATION_RESULTS` outside the repository. All network traffic is fulfilled offline or blocked. It tests both types at 320/375/390/430/768/1024/1440, keyboard, rapid taps, location/client races, issue/success/error/anonymous states and reduced motion. No production or provider calls.

Remaining physical checks: iPhone Safari permission UX, real nearby bin/water GPS, too-far and poor-signal conditions, installed PWA and dynamic browser toolbar/safe-area behavior. Simulated layouts and reported browser accuracy do not prove those physical checks passed.

## Review evidence (2026-10-02)

Build, explicit Razor rebuild/RazorCompile and 33 JavaScript syntax checks passed. Full .NET: 1,490/1,490 (62 confirmation cases); Node: 245/245 (18 confirmation cases). Isolated PostgreSQL: 16/16, including populated migration/rollback and parallel cooldown enforcement. Only the pre-existing MailKit/MimeKit package warnings remained.

New offline browser coverage: 168 public layouts, 70 interaction cases and seven Admin layouts. Existing regression coverage: Home 196 layouts/16 lifecycle/four large datasets; WaterPoint 168 layouts/32 filter/four marker lifecycle/four large datasets; navigation 144 layouts/36 dynamic reset cases; attribution 96; Place icons/public surfaces 198 and gallery 264. Smart Walk and contribution browser suites also passed. Artifacts remain outside the repository in `C:/Codex/epic220`.

The confirmation surfaces measure usable space above attribution, bottom navigation and the active navigation card. Navigation's existing hiding of public marker layers is preserved; the active-navigation popup layout fixture deliberately mounts the real popup renderer as a stress case, without changing layer membership.

Latest local PostgreSQL aggregation measurements: 250 infrastructure/10,000 history rows: 302.13 ms; 500/50,000: 327.48 ms; 1,500/50,000: 238.51 ms; 3,000/50,000: 148.68 ms. Each used three summary queries. These are synthetic local measurements, not production guarantees or timing assertions.
