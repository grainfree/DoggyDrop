# Community bin maintenance (Epic 20.1)

## Audit and transition

The baseline had no report entity or review history. `TrashBin` kept cumulative `FullReports`, `MissingReports`, positive votes and usage counters. Home offered immediate actions; two missing/full reports changed the label, and each missing report deducted up to 12 reliability points (capped at 35). There was no reporter-level duplicate protection, dedicated reporting rate limit, coordinate proposal, photo proposal, or report queue. `BinAction` required authentication but lacked an antiforgery attribute. The new workflow preserves historical counters without interpreting new pending issues as confirmed evidence.

`/Map/Add` remains the canonical, intentionally public new-bin flow. Photo remains optional; ordinary/anonymous proposals are pending, Admin direct curation retains immediate approval. Existing submission/approval rewards remain; ISSUE/PHOTO and lifecycle operations award **no XP**. Existing bin approval notifications remain and new issue/photo decisions send one neutral private notification in the review transaction, without notes or reporter identity.

Admin previously physically deleted through `Reject`/`Delete`; `IsApproved=false` means pending and cannot mean retirement. The owner authorized a dedicated retirement state and additive evidence persistence. `Reject` now retains new proposals using `IsRejected`/`RejectedAt`; approved bins require retirement. The separate authorized, antiforgery-protected `Delete` action remains destructive maintenance and refuses bins with contribution history. It is never called by community review.

## Model and eligibility

`TrashBin.IsRetired` defaults false. Retirement preserves approval, identity, coordinates, image, source, contributor, counters and historical references. `PublicBins()` centrally requires `IsApproved && !IsRetired`. Home, nearby API, nearest/best/list APIs, current planner bin selection, gallery, current city/analytics and leaderboard use it. Historical user contribution totals, Admin views, export and duplicate import audits do not blindly filter history. Nearby approval fan-out explicitly rejects retired bins. Reloading Home obtains the eligible set before clustering; no cluster engine or GPS algorithm changed.

Admin lifecycle confirmation uses a bin snapshot. Reactivation keeps the original record and rechecks current active/pending duplicate candidates. Rejected new suggestions stay visible to their owner and Admin, with a distinct state; they are not a pending queue item. Admin CSV's existing seven-column infrastructure allowlist remains unchanged and includes retained records; it is not a public eligibility feed.

`BinContribution` stores only ISSUE/PHOTO evidence: stable enum reasons, bounded plain text, proposed coordinates/duplicate ID or server-uploaded photo URL, Pending/Approved/Rejected, request ID, bin fingerprint, private submitter/reviewer FKs and review timestamps/note. A new bin is **not** duplicated in this table: the shared Admin landing page links the canonical pending-bin queue. User history links both existing new-bin proposals and their private issue/photo history. Evidence/history lists use bounded pages of 100 entries with previous/next navigation; older records remain reachable. The canonical new-bin queue remains linked separately.

The pattern is source-independent and supports later new evidence types. No OSM special cases, OSM writes, WaterPoint, Place reports or positive-confirmation feature were introduced.

## Review and concurrency

Submission changes no public photo/coordinates/approval/reliability. BIN_MISSING acceptance retires. WRONG_LOCATION acceptance applies coordinates only after current duplicate revalidation. DAMAGED/OTHER resolve without mandatory public mutation. NOT_PUBLIC/DUPLICATE let Admin explicitly choose retirement of the reported record; no merge occurs. Rejection leaves public infrastructure unchanged.

Bin fingerprints cover name, coordinates, image, approval, retirement/rejection, source and owner, excluding changing usage counters. EF concurrency predicates compare the original infrastructure fields, so a raw writer also invalidates a stale decision. Pending contribution status is a concurrency token. A changed bin requires rejection/new evidence rather than silently overwriting newer data. Admin edit, new-bin approval and lifecycle forms carry snapshots. No separate version column is required.

Canonical create/edit/approval, lifecycle and contribution submission/review use the same PostgreSQL transaction advisory lock `(194721,19)` as the municipal importer. Coordinate checks use the existing inclusive distance **<=20m**, against current approved and pending (not retired/rejected) bins. The lock spans revalidation and write. Specialized maintenance tools outside these canonical flows are not a generic infrastructure editor; no claim is made about arbitrary external SQL writers sharing the lock. EF concurrency still protects changed infrastructure rows.

An owner/request unique index makes retries idempotent. A filtered unique index limits one unresolved ISSUE per user/bin/reason; different users may provide independent evidence. PHOTO retries use request identity; photo volume is also rate-limited. Admin decisions cannot process non-Pending contributions again and notifications share the transaction. Replayed new-bin submissions at the same coordinates hit the authoritative duplicate guard.

## Validation and privacy

Issue/photo submission requires authentication and antiforgery. Admin queue/decisions/lifecycle require Admin; review is not subject to public submission quotas. A bounded process-local limiter allows ten community submissions/new-bin requests per user (or anonymous IP) per hour, with no persistent tracking and a 4096-key cap. It uses `TimeProvider` for deterministic tests. This is modest single-instance protection, not a distributed abuse service.

New/corrected coordinates must be explicit, finite and inside the documented Slovenia working rectangle latitude 45.4–46.9, longitude 13.3–16.7. This is a coarse regional extent, not a legal national-boundary polygon. The previous Add model only had numeric Required attributes; this Epic adds actual coordinate and duplicate checks. Decimal points/commas are parsed without thousands separators. Proposed coordinates stay separate. Both Add and correction request geolocation only after a user clicks; no GPS trail is collected.

Photos use the existing strict bin uploader: JPG/PNG/WebP signature and actual Skia decode, 12 MiB, 50 million pixels, 12000px dimension cap, orientation normalization, resized WebP re-encoding. Original bytes/EXIF/device metadata are not copied into the new pixel encoding; SVG is rejected. Existing Cloudinary/R2/local configuration and asset namespaces remain. No provider credentials were read or changed.

Pending images are not assigned to `TrashBin.ImageUrl`. URLs appear only in the Admin review page, never public Home/API or user history. Existing media storage uses unguessable delivery URLs, **not an authenticated/private-asset delivery guarantee**; knowing a raw URL can still allow access at its configured provider. This Epic does not introduce a new private media backend. Users cannot submit arbitrary URLs or transformations. UI asks contributors to avoid identifiable people, plates and unrelated private spaces; it promises no automated privacy recognition.

Public bin output no longer emits contributor names. Source display uses only DataSource name/public website; private notes/contact fields and review identities/notes are absent. A photo approval changes neither `DataSourceId` nor `UserId` and is never labelled field verification. Corrected coordinates are explained by private contribution history, not falsely attributed to the original source. The prior `Status OK` wording becomes `Brez potrditve na terenu` rather than claiming physical verification.

Personal export includes the user's proposal content/status but not private Admin notes, other actors or pending image URLs. Account deletion rejects pending evidence, clears its photo reference and submitted descriptions/identity; accepted public infrastructure remains under existing account-deletion semantics. Reviewer FKs become null when their account is deleted.

## Photo failure handling

Upload failure creates no evidence. Known stale validation after upload attempts reference-safe cleanup. A database/commit failure of unknown outcome retains the new upload for reconciliation instead of risking deletion of a committed reference. Successful approval atomically applies the public photo and reviewed status, then cleans the prior asset only if unreferenced. Rejection atomically records rejection, then cleans the pending asset. Public bin references and other Pending photo proposals prevent deletion, including conservative transform/version aliases. Cleanup failure logs a neutral message, retains the asset and never undoes a successful review. Accepted/rejected history can contain the original proposal URL after safe asset deletion; it is not a promise of permanent photo retention.

No background orphan janitor or legal retention schedule is invented here. Unknown-outcome uploads and failed cleanup require controlled owner reconciliation using storage inventories and current references. Storage is never contacted by the test fakes. Existing Add/Admin-upload failure paths can also retain orphans; they are not silently deleted after an ambiguous database outcome.

## UI and verification

Home preserves navigation and actual BinId targeting after cluster reveal. A missing-photo bin gets a prominent `Dodaj fotografijo` and no empty image placeholder; existing photos get secondary `Predlagaj novo fotografijo`. `Prijavi težavo` opens a focused page, not a large popup form. The global action is `Predlagaj nov koš` and still links to public `/Map/Add`.

Permanent .NET service/HTTP regressions cover origins, pending privacy, text encoding, authorization/antiforgery, explicit coordinates, current duplicates, stale review, photo transitions/cleanup, retirement/reactivation, source/ownership preservation, rate limits and migration structure. Node tests cover explicit geolocation and proposal controls. `Browser/bin-contributions.cjs` uses captured synthetic Razor and offline assets for six widths, photo/no-photo, long source, map action and form layout. Existing cluster/icon/routing/GPS/privacy suites remain relevant. External PostgreSQL probes use a disposable loopback PostgreSQL 17 database and deterministic lock barriers.

Migration: `20261001130821_AddBinCommunityContributions`, additive Up only. Down removes the new contribution table and lifecycle fields and therefore loses new Epic data; use only with a backup/rollback plan. No migration has been applied to an application or production database. No commit, push, deployment, live ORS/tile/media request or production access is authorized for this implementation task.


### Completed local verification (2026-10-01)

Solution build and explicit Razor compilation pass with zero errors (pre-existing MailKit/MimeKit NU1902 advisories remain). Full .NET: 1264/1264; Node: 157/157; new community/Admin browser cases: 54/54; existing cluster layouts: 196/196, lifecycle: 16/16, large datasets: 4/4; icon/public surfaces: 198/198; gallery: 264/264; attribution: 96/96; Home/Explore/Nearby: 60/60; isolated PostgreSQL 17: 8/8; JS syntax: 29/29; whitespace/model consistency pass.

Self-review: no BLOCKER/HIGH/MEDIUM found. The two media operational limitations described above remain: URL-based delivery is not authenticated private-object storage, and failed/ambiguous cleanup requires owner reconciliation. Real-device camera/geolocation and owner-approved backend cleanup checks remain before release. SAFE TO REVIEW; no commit/push/deploy or application migration was performed.

### Strict-review repairs

The independent review subsequently found three gaps in that initial verification: saved plans could restart guidance to retired bins, Home legacy actions omitted the newly required antiforgery token, and City/Analytics counted pending bins from an approved-only collection.

Starting a saved plan now revalidates every bin stop against `PublicBins()` inside the start transaction and shared bin advisory lock. Existing stops store exact coordinates rather than a BinId. Validation therefore requires an eligible bin at the stored coordinates; it never substitutes a nearby bin. A moved, missing, pending or retired destination blocks starting that plan with a Slovenian explanation. Reactivating the same bin at the same coordinates makes it eligible again.

Mixed plans also fail safely if any bin destination is unavailable. This is deliberate: stops share one saved route geometry, so removing a stop would still leave the old route through its location. Partial route rebuilding would require new routing and a separate plan rather than editing historical data. Valid stops, original labels, route geometry, completed walks and the saved plan itself remain untouched. Plans with entirely eligible bin stops, other stop types and ordinary free walks retain their existing behavior.

Home sends its existing server-rendered antiforgery token in the normal form field and binds the intended operation through `[FromForm] binAction`, avoiding MVC's reserved `action` route value. ISSUE/PHOTO still use their protected contribution forms and existing request/duplicate protection. Missing/invalid tokens remain rejected.

City and Analytics retain current-public totals/rankings and separately count pending suggestions (`!IsApproved && !IsRetired && !IsRejected`). Retired/rejected records are neither active nor pending. No new retired metric or pending/retired location payload was added. The repository-wide count audit found these two affected approved-first pending calculations; other pending selectors already exclude retirement/rejection, and account-deletion queries intentionally handle all unapproved records.

Permanent regressions cover eligible/ineligible/mixed saved plans, historical rendering, retirement/reactivation, actual Home form requests and invalid tokens, all issue reasons/retries, the complete OSM photo/correction/retirement/saved-plan/reactivation flow, and nonzero pending counts. The two documented media LOW limitations are unchanged; this repair introduces no migration or storage redesign.

Repair verification: build/explicit Razor PASS; .NET 1288/1288 (including 56 contribution HTTP, 39 contribution service and 89 WalksController tests); Node 161/161; community/Admin browser 54/54 plus actual Home mutation requests 4/4; clustering layouts 196/196, lifecycle 16/16, synthetic datasets 4/4; icon/public surfaces 198/198 and gallery 264/264; attribution 96/96; Home/Explore/Nearby 60/60; JS syntax 29/29; whitespace PASS. Isolated PostgreSQL passed the original 8/8 plus 2 saved-start checks (10/10), including retirement with notification FK access racing a saved-plan start. The shared bin lock is acquired before the user start lock to avoid reversing review lock order. No migration was executed. Focused self-review: BLOCKER 0, HIGH 0, MEDIUM 0, LOW 2 documented media limitations. SAFE TO COMMIT; no commit/push/deploy performed.
