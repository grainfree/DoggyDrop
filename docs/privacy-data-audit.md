# Privacy data audit — Epic 24.0

## Epic 25 factual extension

Activity email adds NotificationPreferences (owner, contribution-updates boolean)
and NotificationOutbox (owner, fixed event type/version, nullable bin/contribution
references, dedup key, status, UTC timestamps, bounded attempts, lease UUID/time,
safe failure category). No address snapshot, email-body archive, private notes,
GPS, IP, tokens or tracking data. Current confirmed email is resolved for delivery;
latest opt-out is respected. Identity security email is separate. No digest,
marketing, welcome or receipt delivery; review results only. Hosted delivery is
off until explicitly enabled in Production; tests/development never use live SMTP.

Preferences and outbox history cascade-delete with account; an already-in-flight
SMTP message cannot be recalled. Owner export includes effective preferences and
event type/status/created/sent, without worker/payload/failure internals. Admin sees
masked recipients and operational metadata with authorized CSRF-protected retry.
No automatic retention cleanup is introduced; long-term retention/provider copies
remain OWNER/LEGAL REVIEW. Privacy/Terms remain drafts and launch/outreach BLOCKED.
See [email-notifications.md](email-notifications.md) for delivery limits and owner checks.

Audit date: 2026-10-04. Baseline: `45fbdf6c75de906ebb78511d302fc51a8b2bca1f`.
This document supersedes the accumulated historical audit notes. It describes repository behavior, not a production inspection or a legal opinion. No production data, credentials or live operational providers were accessed. New public Privacy/Terms text is an explicitly marked draft, not an approved policy.

**PUBLIC LAUNCH BLOCKED. MUNICIPAL OUTREACH BLOCKED.** A technically reviewable change does not remove the owner/legal gates in [privacy-legal-review.md](privacy-legal-review.md).

## Evidence and scope

Primary code: `Data/ApplicationDbContext.cs`; Identity Manage export/delete/password pages; `Services/PersonalDataExport.cs`, `AccountDataDeletion.cs`, `WalkDataDeletion.cs`, `UserMediaCleanup.cs`, `NotificationPrivacy.cs`, `ImageOptimizationService.cs`; `Controllers/HomeController.cs`, `DogsController.cs`, `WalksController.cs`, `CommunityController.cs`, `NearbyController.cs`, `BinContributionsController.cs`, `InfrastructureConfirmationsController.cs`; `Services/WalkingRoutes.cs`, Smart Walk services; `Views/Map/Index.cshtml`, `Views/Walks/Active.cshtml`, `Views/Walks/Planner.cshtml`; `wwwroot/sw.js`, `wwwroot/js/pwa.js`; `Program.cs`. Paths are relative to DoggyDrop/ unless otherwise indicated. Tests use synthetic users and media only.

## Personal-data inventory

“Account lifetime” below describes absence of general automated expiry; it is **not an approved retention period**. Owner/legal decisions are required for every processing purpose and retention category.

| Data / purpose | Storage and audience | Export / account deletion | Retention decision |
|---|---|---|---|
| Account, email, display name, password hash, stamps, roles | Identity DB; owner and necessary administrators; some display names/profile photos appear in community features | Account allowlist and roles exported; hashes/stamps/tokens excluded; account removed | No general automatic expiry; owner decision |
| External login provider/key, optional profile claims | Identity DB; Google login has `SaveTokens=false` | Own provider/key and allowlisted identity claims exported; FK cascade deletion | Owner decision; provider's own account unaffected |
| Dogs, photo, profile attributes, coarse Nearby location | DB; owner, opt-in Nearby/social audience | Own rows exported; dogs and dependent graph removed | Owner decision |
| Walks, exact WalkPoints, times, distance, stops | DB; walk/point/photo controllers enforce owner access, including against friends | Own full points streamed in JSON; walks/points removed on account deletion | No automatic route expiry; owner decision |
| Walk photos/captions and media references | DB + configured media; app pages owner-only, delivery URL may be public | URLs/captions exported, not image ZIP; DB removed; best-effort managed-object cleanup | Provider copies/backups unknown; owner decision |
| Saved Places | User/place junction; owner only | Own favorites and minimal public Place references; cascade deletion | Owner decision |
| Saved plans, stops, route geometry, completions | DB; owner only | Own plan graph exported/deleted | Owner decision |
| PrivacyZone exact center/radius | DB; owner controls, used to exclude community location displays | Exported/deleted; not a public home-location marker | Owner decision |
| Nearby discovery preferences | DB; explicit opt-in center/radius/bins setting | Exported/deleted; switching off removes stored preference | Owner decision |
| Bin submissions and structured issue/location/photo contributions | Infrastructure may be public after review; pending details owner/Admin; Admin notes not public | Own allowlisted contribution data exported, not Admin notes/private proposal photo URL; account deletion clears authorship and submitted description, closes pending proposals and drops pending photo reference | Approved evidence/public infrastructure can remain; owner decision |
| Infrastructure confirmations | Typed bin/water FK, user FK, created time, evidence version; public aggregate only | Own type/target/time exported; user FK SET NULL on deletion, target history retained | 30/180-day display windows are NOT deletion; owner decision |
| Friendships, playdate requests/interests | DB; intended social audience; free text may contain personal data | Own requests/interests and minimal relationship references exported; own graph deleted | Owner decision; no automatic expiry guarantee |
| Notifications, reactions/comments, park visits | DB; notifications recipient-only; walk detail interactions owner-bound; park visit may include coordinates | Own records exported; own graph removed; actor-bearing legacy notifications neutralized without matching PII in free text | No general expiry; owner decision |
| XP/streaks, dog progression, achievements/founder badges | DB; own statistics plus intended leaderboards/aggregates | Own graph exported/deleted | Owner decision |
| Operational logs, cookies, browser storage, provider traffic | Application/process, browser and external providers | Not part of DB export; browser clearing separate; host/provider logs not covered by account deletion | Actual production scope and retention NEEDS OWNER INPUT |
| DataSource contacts/notes, Admin review metadata | Admin-only operational data, distinct from public source name/URL/date | Not copied into public DTOs or unrelated user exports | Owner purpose/access/retention decision |

## Location matrix

| Flow | Precision / recipients | Persistence |
|---|---|---|
| Home map exploration | Viewed tile bounds to configured tile host; no silent walk creation | Map preferences local; server infrastructure is public |
| Nearest bin | Browser location in existing GET to DoggyDrop; query may reach access logs | No dedicated location history from this operation |
| Nearest water | Client compares current eligible WaterPoints | No new server GPS record; directions below if selected |
| Home walking directions | POST via shared server ORS `foot-walking`; exact start, destination, anchors | Request transient; no new WalkPoints, no browser ORS key |
| Smart Walk preview | Server candidates + up to bounded routing requests, origin/anchors to ORS | Protected owner-bound process memory, 10-minute preview; not an account retention policy |
| Saved plan/start | Stops and route geometry | Saved owner plan; active recording only through existing walk flow |
| Legacy Planner | Selected origin, Overpass bounded geographic queries, server ORS | `doggydropPlannerLocation` persists in browser; saved geometry only through existing contracts |
| Active Walk | High-accuracy watcher, server AddPoint; IDs/reason/timing diagnostics | Exact points in DB; unaffected by this Epic |
| Bin/Water confirmation | One-shot current coordinates and accuracy to DoggyDrop | **No exact GPS, accuracy, distance or IP column**; typed evidence only. Accuracy <=25m and distance + accuracy <=50m; does not prove physical presence |
| Nearby dogs/preferences | Explicit opt-in; rounded/coarse public dog position | Coarse DB dog location; separate owner preference/PrivacyZone may be precise |
| Community map | Rounded/grouped activity requiring multiple users; PrivacyZone exclusion | No public individual route; aggregation is not a claim of formal anonymization |
| Photo EXIF | Original source image can include GPS/device/time | New profile/dog/bin normalization emits pixels only; legacy/provider originals are not retroactively certified clean |

## Export, deletion, and media

`POST /Identity/Account/Manage/DownloadPersonalData` is authenticated, antiforgery-protected, current-user-only, attachment JSON with private/no-store/no-cache. It ignores supplied other-user IDs. One consistent DB transaction streams explicit projections in 256-row batches, including all owned WalkPoints without per-walk N+1. It excludes password hashes, security/concurrency stamps, arbitrary token/claim collections, Admin notes and private DataSource metadata. External provider subject IDs and minimal friendship counterpart IDs are deliberate personal-data references, not public disclosure. Export does not contain host logs, backup copies, binary image files or provider-held datasets. New confirmations export excludes internal evidence version and does not invent absent GPS.

Account deletion: authenticated Razor POST + antiforgery + explicit confirmation. Local-password account must verify current password. External-only account uses its existing authenticated Identity owner session, not an invented password or a new OAuth callback. This is **not fresh external-provider reauthentication**. No caller-selected account ID is accepted. Successful deletion signs out the current session; another stored cookie cannot recover a deleted DB user after validation. This Epic does not redesign session architecture.

| Graph | Result after successful DB transaction |
|---|---|
| Identity/account, dogs, walks, points, walk photos, own saved Places/plans/stops/geometry, preferences/privacy zones, private social/progression data | Deleted through explicit service operations/FKs |
| Approved bins / retired bins / formerly approved now unapproved bins with retained contribution or confirmation history | Infrastructure retained, `UserId=null`; hidden objects remain hidden, provenance unchanged |
| Fresh unapproved owned bin without protected history | Deleted |
| Own pending BinContribution | Rejected/closed, proposed photo reference removed, author and submitted description cleared |
| Own other BinContribution history | Author and submitted description cleared; accepted infrastructure photo can remain; Admin review history is not erased wholesale |
| InfrastructureConfirmation | Evidence remains, author FK SET NULL; lifecycle and public summary semantics unchanged |
| Legacy notifications on other accounts | Obsolete walk-start broadcasts removed; actor-bearing known types replaced by neutral metadata/copy; no text matching for identity |
| Photos | After commit only, managed namespaces resolved conservatively, shared references checked using fresh context, unused assets deleted best-effort |
| Provider logs, originals, caches, backups | No proven complete erasure; owner must define operational process and retention |

Anonymous bin submissions have no account ownership link to resolve through an account export/delete request. Public-photo or anonymous-source rights requests therefore need the operator contact process and appropriate verification, not a caller-supplied object ID treated as proof of ownership. Private Admin review notes can remain under the existing history model; their legal retention and any personal content require an owner decision.

A pre-fix relational test showed deletion failed on a RESTRICT FK when an unapproved owned bin retained history. The fix deletes only unapproved suggestions without protected history; retained hidden records lose ownership. No schema changes. Transaction failure rolls back private graph changes; media cleanup occurs after commit. Individual walk/photo deletion uses the existing owner checks and shared-asset protection.

Image repair: profile/dog uploads now require successful decode, orientation normalization and WebP re-encoding on local, Cloudinary and R2 paths, with a 12MiB source cap and existing dimension/pixel limits. Unsupported/corrupt images fail closed, never publish original bytes. Stored filenames are generated, not personal filenames. No backfill or migration of old assets occurred. Cloudinary walk handling already uses normalization/strip transformations; this does not prove provider originals are deleted. Old profile/dog photos are cleanup candidates only after the replacement reference is successfully saved; shared references are preserved. Cleanup failures are neutral logs, not a guarantee of immediate deletion. Concurrent asset-reference assignment remains governed by existing architecture; no persistent cleanup queue was introduced.

### Community moderation and remaining media limits

`BinContributions.ReviewAsync` changes Pending to Approved or Rejected once, requires Admin flow/current bin snapshot and transaction/concurrency checks. Accepted photo replaces `TrashBin.ImageUrl`; old unreferenced managed asset is a cleanup candidate after commit. Rejected photo is also a cleanup candidate. Wrong-location acceptance revalidates coordinates/duplicates; missing-bin acceptance retires; inaccessible/duplicate review can retire deliberately. DataSource and ownership do not transfer. Reviewer notes remain private and no moderation time SLA is implemented.

The existing Admin Map Edit can replace a bin image, but that ordinary replacement path does not itself perform the contribution service's old-image cleanup. No general user self-service public-photo takedown flow or automatic orphan sweep exists. Users can submit an issue/replacement and contact the operator; that is not a guaranteed erasure mechanism. Media uploaded before a later failed DB save may also remain unreferenced. These are operational cleanup/takedown gaps requiring a reviewed owner process, not grounds to bulk-delete assets in this Epic. Provider failure/retries, CDN copies and backups remain outside DB transaction guarantees. Profile/dog replacement coverage was repaired; no claim of universal media cleanup is made.

## Browser storage and cookies

| Mechanism/key | Purpose/content | Lifetime / clearing |
|---|---|---|
| `.AspNetCore.Identity.Application` | Protected login ticket, not cleartext password | Identity default 14-day sliding ticket; persistent browser cookie only where remember-me applies; logout deletes current cookie, stamp validation default interval 30 minutes |
| `.AspNetCore.Antiforgery.*` | CSRF cookie/token pair | Browser session/framework lifetime; not consent |
| `.AspNetCore.Identity.External`, correlation cookies | Temporary external-login process | Framework short-lived flow; Google tokens not saved |
| `.AspNetCore.Mvc.CookieTempDataProvider` | Protected short feedback across redirects | Consumed on read; not an analytics tracker |
| local `doggydrop.homeIntroDismissed.v1` | Intro dismissed flag | Until browser/user clearing |
| local `doggydrop.map.*` | Boolean filters/settings (`showBins`, `showWater`, historical `showParks/showCafes`, `showDogs`, current Places visibility where used) | No automatic expiry |
| local `doggydrop.notifications.*` | Boolean `walkReminder`, `nearbyDog`, `badgeEarned` preferences | No automatic expiry; flags alone are not OS push subscription |
| local `doggydrop.walk.autoCompleteStops`, `doggydrop.walk.vibrateOnStop` | Walk UI preferences | No automatic expiry |
| local `doggydropPlannerLocation` | Legacy Planner latitude/longitude | **Precise location persists** until overwritten/cleared, disclosed; not changed in this scoped audit |
| session `doggydropPlannerGpsTried` | One-session location attempt flag | Tab session |
| session `doggydrop.walk.binAlerts.{walkId}` | Deduplication state for bin alerts | Tab session, includes walk ID |
| Service Worker CacheStorage | Only offline document/CSS shell | Versioned cleanup on activation; no account pages, API, map tiles, uploaded media or request replay |

Search evidence: `Views/Home/Settings.cshtml`, `Views/Map/Index.cshtml`, `Views/Walks/{Planner,Active}.cshtml`, `wwwroot/js/home-intro.js`, `wwwroot/sw.js`, Identity configuration. Old `pwaPromptShown` fixture values do not represent a new active application tracking mechanism. Browser storage is not automatically erased by server account deletion. No new storage or consent banner was added. Legal classification/consent needs owner review; “functional” is a technical purpose, not a legal exemption decision.

## Provider/network inventory

| Provider / path | Information transmitted and trigger | Verified scope / unresolved facts |
|---|---|---|
| Render / PostgreSQL | Requests/application DB and possible infrastructure logs | Architecture known; actual region, logs/backups, sub-processors, agreements NEEDS OWNER INPUT |
| Google OAuth | Requested identity profile/email, provider subject, browser IP; user-triggered login | Server client secret only, SaveTokens=false, existing secure binding; region/role/terms require review |
| Cloudinary | New normalized profile/dog/bin content; existing walk/managed assets; browser media retrieval | Only if configured; no actual active account/region/retention proved |
| Cloudflare R2 | Managed media upload/retrieval | Only if configured; public delivery URL does not imply authenticated access; region/DPA/transfer unknown |
| Local uploads | Normalized files under webroot; browser retrieval | Fallback supported; persistent disk/backups/deletion operations unknown |
| SMTP | Recipient, message, action links including token where needed | No real messages sent; actual provider/region/retention unknown |
| OSM tiles | Browser IP, request headers, visible z/x/y tile area | Repository default/fallback; attribution preserved |
| CARTO Positron | Same tile request data, restricted browser-public key where configured | Opt-in; key is not a server secret; no key inspected/activated; default remains OSM |
| Stadia | Same tile request data | Dormant opt-in, no activation |
| ORS/HeiGIT | Server POST exact requested route coordinates; server IP/API key, no DoggyDrop user identifier in body | Fixed `https://api.heigit.org/openrouteservice/v2/directions/foot-walking/geojson`; key server-only; no live call |
| Overpass | Legacy Planner bounding geography / public POI queries | Not Google enrichment; no live query; no anonymous tracking claim |
| Open-Meteo | Geographic weather query through existing weather integration | No live request; provider legal status/retention unknown |
| Google Fonts, jsDelivr, unpkg | Browser IP/resource requests and permitted referrer | Existing fonts/icons/Leaflet resources; Identity pages use no-referrer. Markercluster locally vendored |

This is a capability/flow inventory, not a list proven active in production. Provider roles, DPAs, regions, SCCs/other safeguards and data residency are all **NEEDS OWNER INPUT**. A server-side route protects the key but still sends route coordinates to ORS. Removing an analytics SDK would not prevent ordinary network disclosures.

## Logging and privacy exposure

No added location/body logging. Profile Cloudinary URL/raw provider error output, raw R2 exception/bucket/key detail and SMTP exception text were removed. Errors are neutral. Existing GPS diagnostics use walk ID/outcome/reason/timing, not new coordinates. IDs are still potentially linkable operational data. ASP.NET, proxy/host, database sensitive-data settings, access logs and provider dashboards need owner review; nearest-bin query coordinates and reset-token URLs can be visible to infrastructure even if application logs omit them. No blanket “no IPs/logs” claim. Identity no-referrer remains. No credential or token was intentionally inspected.

## IP and request-rate data

Identity email actions use process-memory peer-IP partitions (20 requests/15 minutes). Walking routing uses peer-IP partitions (10/minute), plus the existing shared provider budget (30/minute and provider backoff). Infrastructure confirmation uses current-user partitions (20/minute) and a DB-enforced 24-hour per-user/target cooldown. Smart Walk uses owner keys, three generations/minute, bounded active work and preview storage. These counters are not a new persistent IP history; the framework/host may independently log IPs. No raw IPs, route bodies or credentials were added to application logs by this Epic. Production proxy/IP forwarding behavior and log retention need owner verification.

## Retention facts, not policy

No overall scheduled expiry for accounts, routes, plans, photos, confirmations, contribution history or social/progression data was established. Smart preview 10 minutes, import preview 30 minutes and short process-memory rate windows are implementation lifetimes. Confirmation 30/180-day windows control display, not row deletion. User actions can remove some records; account deletion is described above. Backups, logs, rejected photos, old unreferenced media, legal hold and provider deletion need approved durations and operational implementation before a final policy. Restart can clear process previews; this is not a deletion SLA.

## Legal/operational boundary

Owner supplied Jernej Furman s.p., represented by Jernej Furman, Slovenia, and confirmed `admin@doggydrop.app` as monitored privacy contact. Official address was not supplied. No address, legal basis, age limit, approved retention period, provider contract or residency is inferred. `PublicContact:Email` is validated public presentation configuration; SMTP credentials are never used to render it. Missing/invalid contact is graceful. Final rights/supervisory authority details, deadlines/procedure, lawful bases by purpose, children and transfer arrangements await approval. See [privacy-legal-review.md](privacy-legal-review.md) and [osm-data-use.md](osm-data-use.md).


## Local verification record

Final Epic 24.0 checks: build PASS; 1,693/1,693 .NET (31 new cases); 268/268 Node; explicit Razor rebuild and RazorCompile PASS; 31 established standalone/Home/Active/Planner JS syntax checks PASS; whitespace and UTF-8 UI checks PASS. The unchanged MailKit/MimeKit NU1902 advisories are not resolved by this Epic.

Privacy-focused classes: 156 cases, plus two new actual-cookie owner deletion cases. Export projections/ownership: four cases; deletion/media removal: fourteen cases plus transactional rollback coverage. Class groups overlap; do not sum these as additional full-suite tests. Public, private and Admin authorization tests use synthetic identities, no real OAuth/email.

Isolated loopback PostgreSQL: 6 privacy lifecycle/export/media checks, 16 confirmation/concurrency/aggregation checks, 8 Smart Walk checks, all pass. Confirmation aggregation exercised 250–3,000 infrastructure objects and up to 50,000 history rows with three summary queries; measurements are local synthetic evidence, not production guarantees. No new migration or model changes.

Browser fixtures: 224 privacy layout/focus/125%-text/safe-area cases; 56 launch layouts +22 flows; 43 Identity cases; contribution 54 layouts +108 mobile +108 submission +4 Home mutations; confirmation 168 layouts +70 flows +7 Admin; Smart Walk 92 layouts +60 lifecycle +14 saved +18 submission +14 errors +3 XSS +4 geometry datasets; WaterPoint 168 layouts +32 filters +4 marker lifecycle +4 datasets; Place icons/public surfaces 198 +264 gallery renders; navigation 144 layouts +36 resize/reset; attribution 96 layouts; Home 196 layouts +16 lifecycle +4 datasets. Total 2,231 cases, all pass. External requests intercepted; screenshots/captures/logs remain outside the repository under `C:/Codex/epic240`.

To reproduce the new legal-page browser fixtures: set an external `DOGGYDROP_PRIVACY_CAPTURE` directory, run `dotnet test --filter FullyQualifiedName~PrivacySurfaces`, then set external `DOGGYDROP_PRIVACY_RESULTS` and the existing Playwright runtime and run `node DoggyDrop.Tests/Browser/privacy-launch.cjs`. This uses isolated synthetic Razor HTTP captures, never production. Remaining real-device checks include Safari/PWA layout and text size, download behavior, password/external-account deletion UX and supported camera-photo orientation/format handling. No physical-device pass is claimed.
