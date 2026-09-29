# DoggyDrop shared basemap (Epics 19.4 / 19.4.2)

## Current provider model

Leaflet **1.9.4** remains the map engine. All nine map views use the same
`_MapBasemapScripts` / `_MapBasemapStyles` partials and `map-basemap.js` helper:
Home (including navigation), Active Walk, Walk Details, Planner, Place Details,
Admin Place Create/Edit, public Add Bin, and legacy Home. No independent tile
implementation or user preference is added.

Repository default remains **OSM**, with no key or activation setting committed.
Exact, case-sensitive `Basemap:Provider` values are `osm`, `carto`, `stadia`.
Missing/unknown values select OSM. CARTO additionally needs a valid-looking
`Basemap:CartoApiKey`; missing/blank/malformed configuration selects OSM before
any CARTO request. Accepted key configuration is 1–512 ASCII letters/digits or
`-._~`, with no surrounding whitespace. This is a configuration sanity check,
not an entitlement check. A rejected/revoked key is handled by tile failure.
No arbitrary URL can be supplied through configuration.

CARTO Positron uses one labelled raster layer:

```text
https://basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png?key=<browser-public-key>
```

`light_all` is the documented Positron raster style (not `rastertiles/positron`).
It includes labels for orientation; no second labels layer is composed. The
current API-key requirement also applies to this endpoint. It is not an
unauthenticated legacy integration. See the [CARTO style reference](https://github.com/CartoDB/basemap-styles/blob/master/README.md)
and [current CARTO FAQ](https://docs.carto.com/faqs/carto-basemaps).

Stadia Alidade Smooth support remains dormant and unchanged. It is never a
fallback for CARTO. No Stadia key, account or activation is introduced.

## Owner activation AFTER review

In Render's server environment, the owner may set:

```text
Basemap__Provider=carto
Basemap__CartoApiKey=<owner-issued restricted CARTO basemap key>
```

Do not put the actual key in source, appsettings, tests, documentation or Git.
This key is intentionally delivered to browsers through one Razor-encoded data
attribute, only when CARTO is selected. Browser tile requests contain it in the
required `key` parameter. It is **not a confidential server secret** and is not
comparable to the server-only `OpenRouteService__ApiKey`. No other configuration
is serialized. There is no fake client-side security or tile proxy.

The owner reports a Commercial Free key already restricted to production
website/referrers. Its value and dashboard have not been inspected. Keep those
restrictions in CARTO; list every production hostname actually used (apex and
www separately if applicable). Do not weaken them for localhost. A separate
localhost-only development key is optional; otherwise use OSM locally.
Cross-origin tile requests use `strict-origin-when-cross-origin`, allowing an
origin referrer without exposing the page path/query.

The current published commercial free allowance is **1 million tile requests
per calendar month, across account keys**. This is an owner entitlement/usage
assumption, not a promise of unlimited free service. The owner must monitor the
CARTO dashboard and recheck terms/quota. This application has no billing API,
plan upgrade, card collection or payment UI. No paid plan was activated.
Sources checked 2026-09-29: [API keys / allowance](https://www.carto.com/basemaps/apikey/),
[terms](https://carto.com/legal/basemap-terms/).

To disable CARTO, set `Basemap__Provider=osm` or remove the provider setting.
The obsolete `CartoBasemap:PublicApiKey` setting is not read.

## Attribution, resolution and failure behavior

CARTO displays clickable **© OpenStreetMap contributors, © CARTO** credits on
every map; OSM fallback removes CARTO's credit and retains OpenStreetMap's.
Stadia retains its Stadia/OpenMapTiles/OSM credits. Shared attribution is at least
11px, wraps on narrow screens, and retains accessible links and control focus.
[CARTO attribution](https://carto.com/attribution/).

For CARTO/Stadia, Leaflet `{r}` requests a single `@2x` 512px image rendered at
256 CSS pixels on high-DPI screens. `detectRetina:false` prevents four-request
splitting; no zoom offset or custom tile size is introduced. CARTO native zoom
is 20, OSM native zoom is 19. Existing map maximums remain 19 or 20; OSM is
overzoomed only on surfaces already allowing 20. The geographic grid and marker
positions do not change.

After three tile errors over a map's lifetime (network/refusal/quota included),
CARTO or Stadia switches the **same layer** once to OSM, updates attribution and
native zoom, and removes its error handler. No provider chain, duplicate layer,
switch-back, retry loop or payment action occurs. Existing markers/routes,
controls, pan/zoom and Walk Details diagnostics remain attached. OSM failure
leaves the overlays/controls usable over a neutral background; tile availability
is not guaranteed. [OSM's tile policy](https://operations.osmfoundation.org/policies/tiles/)
continues to apply. No offline cache, prefetch, bulk download, proxy or
cache-busting parameters are added; normal HTTP caching remains.

## Privacy and regression boundary

CARTO receives the browser IP/User-Agent, origin referrer where available,
requested tile coordinates/viewed area and the browser-public key. The tile URL
contains only the fixed style, z/x/y, retina suffix and required key parameter.
No UserId, email, dog/WalkId, Privacy Zone, owner or route geometry is appended.
See `privacy-data-audit.md`. No analytics, cookies or localStorage are added;
provider roles/retention/regions/transfers still need owner/legal verification.
The public Privacy Policy is not finalized or changed.

ORS on api.heigit.org, foot-walking, routing budgets, route geometry/styles,
GPS recording/WalkPoints/distance/proximity/Finish/diagnostics are unchanged.
Place data/logos/Featured, filters, Nearby/Community, popups, PWA, SEO, importers,
authentication, public Add Bin and Admin coordinate selection remain unchanged.
No schema/model snapshot/migration changes.

## Verification boundary

Permanent Node tests exercise provider selection, key sanity checks, fixed URLs,
attribution, native zoom/retina options and one-time fallback. HTTP tests render
real Razor with synthetic configuration, prove only the public CARTO key can be
emitted, and preserve active Home behavior. All nine surfaces retain the shared
partials. Browser layout and failure fixtures use intercepted tile responses;
no fake key is sent to CARTO. External fixtures/screenshots are not repository
files. Authenticated production delivery, dashboard restrictions/quota and
real-device tile readability still require owner checks after review.

### Local results — 2026-09-29

- Build and explicit Razor rebuild pass (existing MailKit/MimeKit NU1902 warnings).
- .NET: 1,159/1,159; Node: 120/120. Focused map-popup/basemap HTTP: 46/46;
  permanent basemap Node: 13/13. Routing, GPS, Planner, Places/popups/logos,
  SEO, privacy and importer regression suites are included in the full run.
- 15 standalone JS files and 11 inline blocks pass syntax checks. Home/Planner
  use rendered HTML; Active uses synthetic Razor substitutions. Whitespace passes.
- 54 synthetic layout scenes across 320/375/390/430/1024/1440px pass, including
  attribution hit testing, active/navigation Home states and Admin picking.
- 16 browser failure cases pass (missing/malformed/unknown config, 403, 429,
  network failure, OSM failure too, and retained Stadia fallback; narrow/wide).
- Continuation: 10 supplemental lifecycle checks pass (five each for CARTO and
  dormant Stadia): rapid-zoom cancellation, per-map fallback isolation, native
  zoom 19 at map zoom 20 after fallback, retina size/offset, and remove/recreate.
- Four browser retina/performance cases pass. At 390px, both providers and DPR
  1/2 make nine initial requests and 18 after the same pan/zoom sequence. CARTO
  DPR2 uses 512px source images at 256 CSS pixels. These are mocked request-count
  checks, not real CDN latency/throughput measurements.

**Visual review remains incomplete:** 24 matched captures were made at fixed
public city/neighborhood/building/Place-heavy/bin-heavy/park coordinates, zooms
and 390/1024px widths with synthetic overlays and a sample shop logo. OSM returns
geography, but CARTO's unkeyed public preview returns only an API-key-required
placeholder, even with HTTP 200. The watermark was retained. These images cannot
prove Positron POI reduction, street/path/green-space readability or Admin
building-level precision. They are not evidence that Positron is an empty map.
No production key, production referrer or production data was used; all automated
integration/layout/failure tests intercept tiles without contacting CARTO.

Before production activation, the owner must review actual keyed Positron on the
same scenes (especially z19 buildings and park paths), check route/logo contrast
and real mobile controls, confirm allowed production hosts and Commercial Free
quota in the dashboard. A separately restricted development key is optional;
never loosen production restrictions. Positron is a candidate worth reviewing,
not yet a visually approved replacement. No paid-plan operation, production
activation, commit or deployment was performed in this task.

## Owner-run first keyed visual check (after separate release approval)

This procedure is for the owner; it was not executed by the coding agent. The
production key stays restricted to `doggydrop.app` and any other explicitly
approved production hostname. Do not share its value or loosen its restrictions.

1. Finish code review and separately authorize the normal release. Confirm the
   deployed build includes Epic 19.4.2 before enabling its provider configuration.
   Keep `Basemap__Provider=osm` until the controlled visual-check window.
2. In the CARTO dashboard, verify the existing key's Commercial Free status,
   remaining allowance and exact allowed website hosts. Do not purchase a plan.
   A preview on a different hostname needs its own owner-issued, separately
   restricted preview key; do not reuse or broaden the production key for it.
3. In Render, select the DoggyDrop web service, open **Environment**, and set
   `Basemap__Provider=carto` and `Basemap__CartoApiKey` to the owner's existing
   restricted basemap key. Enter the value privately in Render, not in Git or
   chat. Do not change any ORS setting. **Save only** stages environment values
   without applying them; they take effect on the next approved deployment.
   Once the approved build is present and the owner authorizes activation,
   **Save and deploy** applies them to that existing build. These are owner
   actions outside this task. See [Render environment-variable save behavior](https://render.com/docs/configure-environment-variables).
4. Open a fresh browser session at `https://doggydrop.app/` on the allowed domain.
   For the first check, use ordinary public map exploration, without starting GPS
   recording. Confirm loaded tiles come from `basemaps.cartocdn.com/light_all/`,
   real geography appears without an API-key watermark, and CARTO/OSM attribution
   is visible/clickable. Check tile status and the origin-only referrer locally;
   do not export request URLs/HAR files containing the key or share the key-bearing
   HTML attribute. An OSM map alone is not proof that CARTO activation succeeded.
5. Compare OSM and CARTO screenshots at identical center/zoom/viewport: Ljubljana
   city (46.0569, 14.5058, z13), neighborhood (same center, z16), buildings
   (46.0504, 14.5062, z19), Maribor Places (46.5577, 15.6459, z16), bins (same
   center, z17), and Tivoli park (46.0585, 14.4951, z17), at 390 and 1024px.
   Capture the OSM reference before activation. Also inspect 320/375/430/1440px.
   Check street names, minor walking paths, park boundaries, building edges,
   bins, selected/Featured Place logos, popups, route contrast, attribution and
   bottom-navigation overlap. Do not interpret a watermark placeholder as Positron.
6. Inspect the remaining shared surfaces: Active/Walk Details/Planner using
   owner-approved test records, Place Details, Admin Create/Edit, Add Bin and
   legacy Home. On coordinate pickers, check z19 alignment, click/drag and field
   updates, then cancel without saving. On a real high-DPI phone, verify `@2x`
   tiles, legible text, pan/zoom and controls. Do not create/edit production
   records merely for this visual check.
7. Test fallback in that browser by temporarily blocking only
   `*basemaps.cartocdn.com/*` in DevTools and reloading. After at least three failed
   tile loads, confirm OSM tiles/credit replace CARTO once, with overlays and
   controls intact. Pan/zoom: there must be no switch back or layer accumulation.
   Remove the block and reload to restore a fresh CARTO attempt. Do not revoke
   keys, weaken restrictions or exhaust the quota to simulate failure.
8. Monitor CARTO usage/refused requests after the check. If imagery, restrictions
   or quota cause a problem, restore `Basemap__Provider=osm` through the owner's
   approved Render configuration deployment, then refresh and confirm OSM-only
   tile requests. Saving without deployment is not an immediate rollback.

Technical code safety is separate from this manual visual/operational acceptance.
The continuation reuses the existing implementation and permanent tests; no
provider, GPS, routing or schema changes were needed during the re-review.
All listed build/test/fixture checks were rerun successfully. Technical verdict:
SAFE TO COMMIT, subject to normal code review; no commit was made. There are no
confirmed BLOCKER/HIGH/MEDIUM implementation findings. The one LOW validation
limitation is the outstanding owner-run keyed visual/operational acceptance,
which remains a prerequisite for approving production use of Positron.


## Epic 19.4.3: attribution presentation

The owner reports that CARTO has now been enabled and visually accepted in
production. This supersedes the pending owner acceptance noted above; this task
did not visit production or validate a real key.

The shared helper calls Leaflet 1.9.4 `attributionControl.setPrefix(false)` to
remove only framework branding. Layer credits, links, provider selection,
credentials, URLs, retina settings and fallback logic are unchanged.
[Leaflet documents this prefix option](https://leafletjs.com/reference.html#control-attribution-setprefix).
CARTO still displays “© OpenStreetMap contributors, © CARTO” with its original
links, as required by [CARTO's attribution guidance](https://carto.com/attribution/).
OSM alone displays “© OpenStreetMap contributors”; fallback removes stale
CARTO/Stadia credits. Dormant Stadia retains all three provider links.

Previously the shared style used 11px text, 3px/6px padding and a 94% white
background; some views overrode this with a large pill corner and 78% white.
The new shared treatment uses 11px text, 2px/5px padding, a 90% white background,
muted green/grey text, an understated 4px corner and underlined links. Existing
keyboard focus remains visible. No attribution is hidden, clipped or faded out.

Bottom-right remains the default attribution position. Home's existing clearance
states are preserved. Local browser fixtures exposed two mobile overlaps:
Active Walk's action stack and Planner's bottom navigation. At widths up to
575.98px, shared CSS reserves 68px on the right for Active Walk. Planner's
bottom-right corner also conflicts at intermediate tablet widths, so its credits
use the clear lower-left strip below 992px, with a 10px inset and 96px plus
safe-area bottom clearance (68px below 721px). Desktop remains bottom-right.
These attribution-only offsets preserve controls and keep credits inside maps.

### Permanent offline browser regression

`DoggyDrop.Tests/Browser/map-attribution.cjs` requires Playwright and a locally
installed compatible browser. `LEAFLET_TEST_ASSETS` points to an offline asset
folder containing Leaflet 1.9.4 `leaflet.js`/`leaflet.css` and Bootstrap Icons
1.11.3 `bootstrap-icons.css`/`bootstrap-icons.woff2`. Do not use provider keys.

1. Set `DOGGYDROP_POPUP_CAPTURE` and `DOGGYDROP_WALKING_CAPTURE` to the same
   absolute temporary folder outside the repository; run `dotnet test` to
   capture synthetic HTTP-host Razor pages. These hosts use local test databases
   and fake providers, not the application's production startup.
2. Set `ATTRIBUTION_RESULTS` to another absolute temporary output folder.
   Set `LEAFLET_TEST_ASSETS` as above. Make `playwright` resolvable through the
   local Node installation (or `NODE_PATH`). Optionally set `BROWSER_CHANNEL`
   to an installed channel such as `msedge`.
3. Run `node DoggyDrop.Tests/Browser/map-attribution.cjs` from the repository.
   No HTTP server is needed: every request is fulfilled from local assets,
   synthetic SVG tiles or captured HTML, or aborted. Link clicks are intercepted
   to test hit targets without navigating to providers.

The 96 cases cover 16 scene variants at 320, 375, 390, 430, 1024 and 1440px:
Home (including an open Place popup, navigation/walk/nearest states and OSM),
Active Walk, Walk Details, Planner, Place Details, Admin Create/Edit, Add Bin
and legacy Home. Home, Planner and Admin Edit use captured Razor markup;
Admin Create exercises the shared editor form. The other surfaces are isolated
current-view CSS/markup fixtures, not full application end-to-end sessions.
Active Walk includes the real collapsed cockpit/action markup and measured
cockpit clearance. Checks cover visible bounds, provider credits, no Leaflet
prefix, click hit-testing, keyboard Tab focus and critical-control overlap.
All 96 cases passed. Five representative screenshots are written to the result folder.

Verification for this change: build and explicit Razor rebuild passed;
1,159/1,159 .NET and 122/122 Node tests passed (15 basemap tests). Standalone
JavaScript and Home/Active/Planner inline syntax checks passed. External offline
fixtures also cover 16 tile-failure cases, four retina/performance cases and ten
CARTO/Stadia lifecycle cases. No real tiles, credentials or production data are
needed for these tests.

Physical-device follow-up: confirm iOS/Android browser chrome and safe-area
insets, expanded Active Walk panels, touch targets, landscape, and enlarged
system text. Synthetic desktop-browser fixtures cannot replace those checks.

An additional 600/720/768px Planner probe confirmed usable attribution after
these offsets. At 768px the page itself still has 24px horizontal overflow in
the isolated captured-page fixture. The same 792px document width at a 768px
viewport occurs with the baseline stylesheet, so this is a pre-existing Planner
layout observation, outside attribution polish; no general page layout was
changed. The six required viewport widths have no overflow failures.
