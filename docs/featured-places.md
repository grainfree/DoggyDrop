# Epic 20: Featured Places foundation

Featured is promotional prominence, not a recommendation, quality rating or verification. Normal Places remain public and equally functional. No payments, ownership, analytics or campaigns are introduced.

## Configuration and time

Place stores only IsFeatured (default false), FeaturedFrom and FeaturedUntil (nullable UTC timestamps). The additive AddPlaceFeatured migration adds only these three columns to Places; existing rows default to false. Down removes only those columns. No new index: the small public catalogue already needs all active supported Places, and an index on the time-dependent presentation flag would not avoid that scan.

FeaturedPlaces owns the shared SQL/in-memory projection. Current means enabled, active, one of the five commercial/service categories, From null or <= now, and Until null or > now. The interval is [From, Until). Public controllers use the existing injectable TimeProvider and capture one UTC instant per query. Expiry does not mutate configuration. Inactive Places retain configuration but never render Featured.

Admin Create/Edit uses the existing form and save path, including UpdatedAt optimistic concurrency and its atomic Place/amenity save. Edit carries OriginalUpdatedAt in a hidden field using invariant UTC round-trip format (including PostgreSQL microsecond precision). Missing, malformed or stale versions are rejected before validation/upload; EF also uses that original form version in the UPDATE predicate to protect the interval after the initial check. Ordinary validation redisplays preserve the original token. A conflict removes the editable form and offers an explicit reload link with the Slovenian conflict message; only a new GET supplies current data/version. Create needs no token. There is no separate Featured write endpoint. The checkbox defaults off. Dates are optional and use Slovenian local time (Europe/Ljubljana); strict server parsing converts to UTC. Nonexistent spring-forward times and ambiguous autumn times are rejected with Slovenian guidance, rather than silently choosing an offset. Equal/reversed intervals are rejected even when the flag is off. Internal UTC scheduling fields are not bindable input properties.

The UI disables and clears controls when moving to DogPark/DogBeach (or no supported category). The server rejects an attempt to enable Featured for an ineligible category. Saving an ineligible category with the flag off clears both dates, so no hidden promotion configuration remains. Eligible disabled Places may retain dates for later administration. Admin shows the configuration and explains the interval; no redundant status enum is stored.

SetActive retains its current-state command semantics: an actual activation change advances UpdatedAt. Bulk Place tools also operate on current state and advance UpdatedAt, without requiring an Edit-form token. Both invalidate previously opened Edit forms. Stale requests perform no logo upload. A race after upload rolls back all database edits and runs existing best-effort reference-aware cleanup; a winning logo reference is retained.

## Public behavior

Discovery renders a small text label, Izpostavljeno, and a subtle border/background. Its title explains promotional prominence. Without location: current Featured first, then Name and Id within both groups. Existing category filters preserve this ordering. With client-side location: nearest-first remains primary, with existing server order only resolving equal distances. There is no near-distance promotion rule.

Saved Places deliberately keeps its existing newest-saved ordering and minimal presentation without a Featured badge. Details uses a small text label while preserving the main navigation action and all information. Home markers retain their size, logo/category pipeline and z-index. A subtle ring appears on current commercial Featured markers; selected styling takes priority. The popup supplies textual disclosure. placeId focus, bins and navigation logic are unchanged.

Only derived IsCurrentlyFeatured is added to public DTOs. Scheduling, Admin verification metadata and DataSource details are not exposed. Featured neither changes amenity facts/verification nor follows from Partner provenance.

## Queries, caching and scope

The expression is translated into SQL; no per-Place lookups are introduced. Anonymous Discovery remains one Place query; signed-in Discovery retains its one existing Saved query. Details preserves its existing amenity projection. PostgreSQL 17 checks exercise query translation, exact boundaries, ordering and overlapping edits on isolated local databases built from the model, without executing migrations.

Places pages already use no-store responses; Home now explicitly does so too. No response/output-cache middleware or service-worker HTML cache was found. Status is recomputed on each request. An already-open page is a snapshot until navigation/reload; no polling, timer, scheduling payload or background job is added.

No GPS, Walks, bins, bin-photo processing, importer, Saved semantics, DataSource semantics, amenity semantics, Nearby, Community, privacy-zone or authentication-configuration changes. The Epic migration is generated but NOT applied. No production data or live image providers are accessed.

## Verification

Tests cover deterministic interval and eligibility rules, local/UTC and both DST transitions, invalid ranges, enable/disable/category changes, preserved independent facts, overlapping Featured saves, sequential two-GET stale forms (Featured, amenities and unrelated fields), validation redisplay, activation/bulk invalidation, missing/malformed tokens and logo races, public projection/query count, Saved order, actual HTTP authorization/antiforgery/overposting and rendered disclosure/expiry. Node tests cover marker treatment/selection, nearest-first composition and Admin category controls. Offline rendered-page layout checks include 320, 375, 390, 430, 1024 and 1440px. Remaining manual checks: native date/time inputs on iOS/Android and real-device map marker focus/navigation.

Final local results: build PASS; 708/708 .NET (64 added cases, including 20 for the stale-form fix), 82/82 Node (5 added cases), 35/35 isolated PostgreSQL checks, 36/36 offline layout cases (30 original plus 6 conflict-page cases), 12 JS files plus Home/Active inline syntax, explicit Razor compilation and tracked/untracked whitespace checks PASS. Existing package/nullability warnings remain. PostgreSQL was stopped after the checks. No commit or push was made.
