# DoggyDrop basemap (Epic 19.4)

## Pre-change audit — 2026-09-28

Baseline: clean `main`, `638d68917a05ec6148c5124d4c4b63f8c251448c`.
Every map uses Leaflet 1.9.4 and raster PNG tiles, not client-styled vector geography.

| Surface | Previous basemap | Maximum zoom / retina |
| --- | --- | --- |
| Home `Map/Index`, including Navigation Mode | Shared helper: CARTO Voyager without labels plus a second label layer at 0.72 opacity when `CartoBasemap:PublicApiKey` exists; otherwise OSM | 20; OSM native 19; CARTO `{r}` |
| Walk Planner preview | Shared helper: keyed CARTO Voyager or OSM | 20; OSM native 19; CARTO `{r}` |
| Place Details | Same helper as Planner | 20; OSM native 19; CARTO `{r}` |
| Active Walk | Hard-coded OSM | 19; standard 256px |
| Walk Details (owner route only) | Hard-coded OSM | 19; standard 256px |
| Admin Place Create/Edit (shared editor) | Hard-coded OSM | 19; standard 256px |
| Public Add Bin | Hard-coded OSM | 19; standard 256px |
| Older `/Home/Index` map | Hard-coded OSM | 19; standard 256px |

Old tile templates: `https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png` and
`https://{s}.basemaps.cartocdn.com/rastertiles/{voyager|voyager_nolabels|voyager_only_labels}/{z}/{x}/{y}{r}.png?key=...`.
Old attribution was plain OpenStreetMap text, plus CARTO for its layers. Home and
Active applied raster color filters. Most views carried an incorrect Leaflet CSS
integrity hash; Home and Admin Edit had already corrected it in Epic 19.3.
Municipal Bin Import and Places Import previews contain tables/links, no Leaflet maps.
There is no separate navigation basemap: navigation uses the Home map.
No map account or production entitlement is confirmed by the owner. No production
configuration or private credentials were accessed.

## Provider decision

Use **Stadia Maps Alidade Smooth raster**, optionally enabled after owner setup.
It is a provider-designed light style, not an original bespoke tile style. Its muted
land/green areas, light roads, restrained labels and reduced POIs suit the written
DoggyDrop brief. Geography is rendered by the provider; CSS does not recolor tiles.
The owner supplied the project illustration during the audit: pale sage land,
muted green natural areas, white roads and dark teal route/markers. This guides
hierarchy rather than literal geometry or pixel equivalence. Real street names,
paths and buildings must remain legible; the preset is not an exact palette match.

Alternatives considered:

- CARTO Positron is a practical muted raster alternative to existing Voyager.
  CARTO requires an owner-issued key and visible attribution. Current terms list
  a 1M monthly commercial free quota; entitlement/usage would still need owner
  confirmation. Positron is less oriented to green walking context than the selected style.
- MapTiler supports custom raster styles and browser-public restricted keys. Its
  free plan is for testing/personal/non-commercial uses; Flex currently starts at
  USD 30/month. A custom style would offer finer palette/POI control but adds owner
  style publishing and account setup.
- Self-hosted styled raster tiles keep Leaflet but introduce tile data, rendering,
  hosting and update operations outside this Epic.
- Leaflet vector plugins add renderer/style/label complexity without a compelling
  need here. No MapLibre, Mapbox GL or Google engine/plugin is introduced.

Sources checked 2026-09-28 (recheck pricing/terms before activation):
[Stadia style](https://docs.stadiamaps.com/map-styles/alidade-smooth/),
[authentication](https://docs.stadiamaps.com/authentication/),
[pricing](https://stadiamaps.com/pricing/),
[terms](https://stadiamaps.com/terms-of-service/),
[EU endpoints](https://docs.stadiamaps.com/eu-gdpr-endpoints/),
[CARTO terms](https://www.carto.com/legal/basemap-terms/),
[MapTiler pricing](https://www.maptiler.com/cloud/pricing/),
[OSM tile policy](https://operations.osmfoundation.org/policies/tiles/).

## Activation and ownership

Default/missing/invalid `Basemap:Provider` uses OpenStreetMap. Set environment
variable `Basemap__Provider=stadia` **only after** arranging an appropriate Stadia
plan and registering the real production domain. No account, paid subscription,
domain registration or deployment is performed by this change. Old
`CartoBasemap:PublicApiKey` is no longer consumed by map pages; remove obsolete
configuration separately after owner verification. Do not paste a private key into
the new provider setting; there is no API-key configuration in this integration.

Stadia domain authentication avoids shipping a token. The currently listed Starter
plan permits commercial use at USD 20/month with 1M credits/month; raster requests
consume credits according to the provider's current schedule. Confirm volume,
overages and spending controls with the owner. The free plan is non-commercial;
do not assume it licenses DoggyDrop production. Localhost development is permitted
without a key but rate-limited. Satellite is intentionally omitted: extra licensing,
cost and controls are unnecessary for the primary basemap scope.

## Network and visual limitations

Tile requests inherently reveal IP, browser metadata, origin and viewed tile area.
They do not append DoggyDrop UserId, email, dog name, WalkId or route geometry.
Stadia uses the documented EU tile endpoint; this is a technical routing choice,
not a conclusion about all subprocessors, transfers or GDPR compliance. Owner/legal
verification of the provider relationship remains required. The Privacy Policy is
not finalized or edited. Tile referrers use only the page origin cross-origin.

Retain clickable attribution to Stadia Maps, OpenMapTiles and OpenStreetMap for
Stadia; OSM attribution for fallback. One raster layer includes labels. `{r}` loads
one 512px image at 256 CSS pixels on retina displays, rather than four tile requests.
OSM has no native retina variant here. Preserve prior surface zoom limits; OSM
native zoom 19 is overzoomed on surfaces allowing zoom 20. Browser HTTP caching
is used normally; no proxy, offline tile cache, prefetch, archive or bulk download.
OSM is best-effort with no SLA and its usage policy applies even as fallback.

No persistent/account preference or switcher is added. The light style still needs
real-device review for minor paths, buildings, labels and marker/route contrast.
MapLibre might merit a separate future Epic for dynamic layer/language styling,
but is unnecessary to display styled raster imagery through Leaflet.

## Implementation and failure behavior

All nine map views use shared `_MapBasemapStyles` and `_MapBasemapScripts`
partials and the small `map-basemap.js` helper. The server exposes only the
allowlisted provider name, never arbitrary configuration, a URL or a key. Leaflet
remains 1.9.4. Shared CSS corrects the duplicated Leaflet CSS integrity hash,
styles readable attribution and keyboard focus, and supplies an empty-map
background. The old geographic image filters are removed.

After three Stadia tile errors during a map's lifetime, the same Leaflet tile layer
switches once to OSM, updates attribution and native zoom, and removes its fallback
handler. Keeping the layer object preserves Walk Details load/error diagnostics.
There is no automatic switch back or custom retry loop. If OSM also fails, the map
keeps its neutral background, markers, route and controls without throwing. This
fallback is best-effort and remains subject to OSM's usage policy.

GPS collection, persistence, distance, Finish, route styling, geolocation, marker
sizes/categories/logos/Featured states, popups, filters and importer semantics are
unchanged. Admin coordinate selection and existing route fitBounds remain intact.
No controller, model, schema, authentication or application configuration file is
changed. There is no migration or added basemap preference storage.

## Local verification — 2026-09-28

- Build and Razor compilation pass; 1,081/1,081 .NET and 98/98 Node tests pass.
- All 14 standalone application JavaScript files pass syntax checks; Home/Active
  inline JavaScript checks pass. Git whitespace checks pass.
- Provider regressions cover default/invalid configuration, fixed tile URLs,
  attribution, preserved surface zooms, retina/referrer options, one-shot fallback,
  all map surfaces and continued route/marker initialization. Four additional HTTP
  cases verify rendered allowlisted configuration and active-walk Home markup.
- 54/54 responsive fixture cases pass: nine scenes at 320, 375, 390, 430, 1024
  and 1440 pixels. They check attribution bounds/clickability, controls and Admin
  coordinate updates. Home and Admin use rendered test-page markup; the standalone
  Active scene is a representative route fixture, not a physical GPS session.
- 8/8 real-Leaflet failure cases pass with mocked tile responses, covering missing
  and invalid configuration, Stadia fallback and total tile failure. Automated OSM
  requests are intercepted; these checks do not fetch public OSM tiles.
- Seven finite localhost visual cases use actual Stadia raster responses: regional
  zoom 8, city zoom 13, neighborhood/Places/route zoom 16 and bin/Admin zoom 17.
  All pass without JavaScript errors. At DPR 2, retina tiles are 512px images
  displayed at 256 CSS pixels. Sample tile responses advertise a six-hour HTTP
  cache lifetime. This is functional integration evidence, not a provider benchmark.
- Screenshots and fixture/probe code live outside the repository under
  `C:/Codex/epic194`; no external harness, tile archive or generated build output is
  part of this Epic's repository changes.

Visual review confirms a quiet, cooler grey-green preset rather than the warmer
sage illustration's exact palette. Generic business POIs are restrained by the
provider style; there is no per-feature raster styling. Parks, paths, streets,
building outlines and subdued water retain useful orientation. Existing teal/green
markers, white popup cards and the active route remain prominent.

Remaining manual checks: physical iOS/Android touch and GPS visibility recovery,
slow-network pan/zoom responsiveness, and production-domain authentication/quota
after owner setup. No production availability or real-device performance claim is
made. No account, subscription, deployment, production access, commit, push or
migration was performed during this implementation.
