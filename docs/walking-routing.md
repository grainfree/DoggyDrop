# Walking routing (Epic 19.5)

## Surface audit and chosen behavior

Leaflet 1.9.4 renders maps; it does not select a routing graph. The basemap and its attribution/fallback are unchanged.

| Surface | Before Epic 19.5 | Current behavior |
| --- | --- | --- |
| Home Navigation, including bins and Places | Browser GET to public OSRM `/route/v1/foot`; the URL did not prove the public server used a pedestrian graph | Browser POST to same-origin `/api/walking-route`; server uses ORS `foot-walking` only |
| Nearest bin | `GetBestBin` scores Haversine distance and reliability; legacy `GetNearestBin` uses squared coordinate distance | Selection is unchanged, not a shortest-walking-route search. Navigation to the selected bin uses ORS |
| Place popup `Navodila`, Place Details | Internal Home navigation by coordinates / `placeId` | Same internal destination flow; no Google Maps handoff or external directions origin URL |
| Planner preview | Generated geometry on GET; external routing on save/start with current location | Preview/save/start use the shared ORS walking service after existing stop selection. Current-location selection still uses Overpass; regional/local fallback uses existing generated stops |
| Planner provider chain | Optional ORS foot-walking, then unverified public OSRM, then generated geometry; estimated speed used for ETA | ORS foot-walking only. Provider distance/duration used. Missing duration stays unavailable |
| Active Walk / Walk Details | Recorded WalkPoints and optional saved-plan overlay | Recorded trail unchanged and never snapped. No provider calls added to either view |
| Discovery, Nearby, nearest preview | Geographic distance / proximity | Unchanged; direct distance is not walking route distance |

## Failure and presentation

A missing key, invalid/oversized request, timeout, quota response, malformed response or unavailable route never falls back to OSRM or driving. Home retains its orange dashed direct line, explicitly labelled as approximate direction and air distance with no walking ETA. Successful routes retain the green line and show “Peš”, ORS distance/duration and provider/data attribution. Existing route request sequencing and cancellation discard stale results.

Navigation requests occur at start and the existing >=60 m movement trigger while a routed result is active, not on every GPS callback. A failed result ends automatic rerouting; stopping and starting navigation is the explicit retry. No polling or timed retry loop. Existing geolocation watchers, arrival/proximity thresholds, GPS persistence, AddPoint, distance recording, sampling/coalescing, Finish and diagnostics are unchanged.

Planner fallback geometry is orange/dashed, labelled “Približna geometrijska razdalja” and “niso preverjena pešpot”; no walking ETA is invented. A bin is labelled a selected stop, not proof of a traversable route. New approximate plans still save their owned stops/options, but do not save generated lines as `PlannedWalkRoutePoints`: the existing storage has no routing-provenance column and such lines must not later appear as a routed walking overlay. Successful ORS geometry is saved through the existing plan mechanism. Plan ownership/authorization and stop ordering are unchanged. Existing historical plans are not rewritten or re-certified as walking routes; previously saved overlays may contain old geometry.

ORS walking data is not a guarantee of current access, safe crossings or dog access. Respect signs, restrictions and actual conditions. Dog-friendly optimization for shade, surfaces, quieter streets and dog access is future work, not part of this change.

## Configuration and owner setup

The pre-existing ORS integration expected `OpenRouteService:ApiKey`, with aliases `OpenRouteService__ApiKey`, `OPENROUTESERVICE_API_KEY`, `ORS_API_KEY`. These remain supported. Repository appsettings and deployment-facing references establish no production credential or active ORS plan. The inspected local process had no value for those environment aliases. No credential was printed, changed or rotated; production and user secrets were not accessed.

**Owner step:** in the DoggyDrop Render web service's environment settings, verify or set the server-only secret **`OpenRouteService__ApiKey`** to a valid key from the owner's approved ORS account. Do not put it in appsettings, client JavaScript, URLs or documentation. Confirm the account permits this use and its current request limits before normal use. Production configuration cannot be proven from this repository. Without a key, route calls return unavailable safely, with the labelled approximation described above. No provider account, credential, paid plan or deployment was activated by this task.

## Request protection and operational assumptions

- Anonymous Home route endpoint requires ASP.NET antiforgery and is POST-only, no-store, with a 4 KiB body limit. Inputs require valid latitude/longitude. Per remote IP: 10 requests/minute, no queue.
- Home and Planner share a process-wide budget: 30 provider attempts/minute and 1,800 per 24-hour window. These are application ceilings, not a claim about the owner's ORS plan. A provider 429 imposes a shared Retry-After cooldown (60 seconds to 24 hours).
- At most 12 ordered coordinates, at least two distinct points, at most 100 km total direct segment distance. Fixed HTTPS provider host/profile; no arbitrary URL/profile input. Coordinates rounded to six decimal places.
- Eight-second cancellation deadline, response limited to 2 MB and 50,000 points. Invalid geometry/distance fails closed; absent/invalid duration does not become a speed-based estimate.
- No automatic provider retries, persistent route cache, new database storage or migration. The budget stores only counters/times. ASP.NET per-IP limiter state is in process memory. Process restarts reset limits; multiple instances would have separate budgets. Current owner-confirmed deployment is one instance without autoscaling. Reverse-proxy IP handling may group visitors; the global budget remains authoritative protection. Reassess these assumptions if scaling changes.
- The key stays in the server Authorization header. Named provider HttpClient loggers are removed; operational logging records HTTP status or exception type only, without bodies, coordinates or key values.

Provider references: [ORS directions](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/), [restrictions](https://openrouteservice.org/restrictions/), [account plans](https://account.heigit.org/info/plans), [terms](https://account.heigit.org/info/tos). The owner must verify the actual account quota and commercial-use/attribution obligations; public documentation is not evidence of the deployed account. The old [OSRM API](https://project-osrm.org/docs/v5.24.0/api/#general-options) profile is tied to server preprocessing, so a `/foot` URL alone was not sufficient assurance.

## Privacy and verification boundary

Home sends only origin/destination coordinates in a same-origin POST body (plus the normal app cookie/antiforgery context to DoggyDrop). The server sends ORS only ordered coordinates and fixed routing options. ORS sees the server's IP and account authorization, not a forwarded browser IP, UserId, email, dog name, WalkId, Place name or Privacy Zone. Intermediate Planner stops are coordinates only. Provider processing/retention, account roles and international transfers still require owner/legal verification; see `privacy-data-audit.md`.

Existing Planner GET origin parameters and nearest-bin GET parameters may still occur in browser history/infrastructure logs; this Epic removes routing coordinates from the external Home URL, not those separate existing flows. Overpass discovery and tile requests remain distinct flows. No new analytics, cookies or localStorage are introduced by routing; the existing Planner location prefill is unchanged.

Permanent tests cover the exact foot-walking POST, minimal payload, provider metrics, missing key, malformed data, errors, cancellation, 429 cooldown/budgets, antiforgery, endpoint rate limit, real Planner Razor labels, ordered stop routing and save/start geometry provenance. Live ORS calls are not mandatory tests and were not made. Synthetic mobile fixtures exercise successful and approximate Home/Planner states at 320/375/390/430 and desktop widths. Device permission behavior, real pedestrian access/route quality and production account setup remain manual owner checks.
