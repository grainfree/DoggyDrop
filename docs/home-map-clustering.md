# Home map location clustering

Home uses one combined Leaflet.markercluster group for public bins and public Places. A shared count avoids overlapping bin and business-logo clusters. Database records, coordinates, categories, approval, Featured state and reliability are not changed. A cluster means nearby locations, not duplicate records.

## Layer inventory

| Home marker/overlay | Treatment |
|---|---|
| Public TrashBins (all report/reliability states) | Cluster while their existing Bins toggle is enabled |
| Public Places, including category icons, managed logos and Featured | Cluster while Places is enabled; no logo/Featured exemption |
| Selected bin / open Place popup / `binId` or `placeId` / Explore or Saved focus | One original marker temporarily outside the cluster, with its existing selected treatment |
| Navigation destination | Existing independent navigation marker; never clustered |
| User location | Existing independent user pane; never clustered |
| ORS navigation route / approximate direct line | Existing independent polyline; never clustered |
| Home active walk recorded trail / planned route | Existing independent polylines; never clustered |
| Nearby dog markers | Existing optional social layer; outside public-location counts |
| Community hotspot count markers and heat circles | Existing community layer; outside public-location counts |
| Temporary coordinate pickers | None on Home; unchanged on precision maps |
| Retired P/C/W POIs | Remain retired; not reintroduced |

Planner has sparse, meaningful stops and route geometry, not Home's national location dataset. It remains unchanged. Active Walk's dedicated page, Walk Details, Admin Create/Edit, Add Bin, Place Details mini-map and legacy Home do not load clustering assets.

## Library and assets

Leaflet remains 1.9.4 with the existing versioned CDN/SRI policy. Markercluster 1.5.3 is pinned and vendored under `wwwroot/lib/leaflet.markercluster-1.5.3`, with its MIT licence. Only Home loads its script, base animation stylesheet and DoggyDrop's custom stylesheet; local assets use `asp-append-version`. No new CDN, network provider, runtime package dependency, storage, telemetry or credentials.

Sources checked 2026-10-01: [official project/API](https://github.com/Leaflet/Leaflet.markercluster), [changelog](https://github.com/Leaflet/Leaflet.markercluster/blob/master/CHANGELOG.md), [security page](https://github.com/Leaflet/Leaflet.markercluster/security), and npm registry's published `latest` metadata (1.5.3). This is a mature Leaflet 1.x plugin with slow releases, not a claim of frequent active maintenance. The project lists no published security advisories and no security policy; this is not proof of absence of vulnerabilities. The integration uses the established clustering/spiderfy engine instead of a new spatial engine. New cluster content is a numeric count written with `textContent`; no source HTML, SVG or names are interpolated into it. The plugin adds no network calls.

Original downloaded SHA-256 values:

- `leaflet.markercluster.js`: `1e4e1d22972a3926f48598e0caf14e3fe7049835d428a344fed4f9e3665b3508`
- `MarkerCluster.css`: `614dea0a98ff3f4ead74f04918f6b1d1b9ba435c25b5fc23b21a394d1e3e4d87`
- `MIT-LICENCE.txt`: `3133dacd38250bd1f524d8166cc60a016a382522e062fce143691c65bf5e4b3d`

## Zoom and visual behavior

Count bubbles use pale green fill, green border, dark centered text and a subtle shadow. Sizes are 44 px for fewer than 10 members, 48 px for 10–99 and 52 px for 100+. No embedded category icons or logos. Existing individual marker dimensions, anchors, category artwork and popup content remain unchanged.

- Zoom <14: 64 px cluster radius; unselected artwork scales to 85% inside unchanged hit boxes.
- Zoom 14–16: 48 px radius; unselected artwork scales to 95%.
- **Zoom 17+: broad density grouping ends**, radius shrinks to 22 px and artwork returns to full size. Only close/overlapping markers group.
- No hard `disableClusteringAtZoom`: coincident markers must remain accessible. At maximum Home zoom 20, remaining overlaps spiderfy using the plugin's normal click/Enter interaction. The existing basemap maximum stays unchanged.

Cutoffs 16, 17 and 18 are compared by the permanent browser fixture using the same 80-point dense grid at zooms 14/16/17/18/20. A cutoff of 18 unnecessarily retains grouping at zoom 17; 17 balances neighborhood density with street-level individual access. Maximum-zoom viewport culling explains why some distant grid points are not rendered; they remain members, not lost records.

Animations are disabled for predictable bulk updates and to avoid delayed marker transitions during selection/filter changes. Cluster clicks still conventionally zoom to member bounds; coincident groups can spiderfy earlier when further zoom would not separate them. Visible artwork scaling never changes the 44×48 bin or 52×52 Place interaction boxes. Navigation/user markers are not scaled. Selected styling remains full-size.

## Filters, focus and lifecycle

`home-map-locations.js` creates logical Leaflet layers for bins and Places. Their existing `addTo`, `remove`, `map.hasLayer` and `getLayers` contract is retained, while only enabled members enter the combined group. Toggling a layer off removes its count contributions and any focused member. Navigation temporarily removes these layers and restores the same prior set when stopped.

Home currently has Bins/Places map toggles. Category tabs in Explore filter the list only, and there is no Home Featured map filter. Their semantics are unchanged. The shared layer's `setLayers` replaces member sets and supports later category/Featured filtering or refresh without stale counts, duplicates or accumulating cluster groups. It does not add any new filtering UI. Current public bins/Places arrive in the page payload; bin-action responses update the existing marker icon. Nearby/community refresh remains independent.

Explicit focus removes the original marker through the plugin's supported `removeLayer`, which restores spiderfied coordinates, and puts it in a single focus layer. It then centers at at least zoom 16 and opens the existing popup/bin detail. This synchronous path avoids late zoom callbacks reopening old targets. The focused marker remains visible when zooming out; closing it returns it to clustering. A hidden/deleted member cannot be revealed. For Place deep links, startup geolocation still updates the user's location marker and nearby suggestions, but does not recenter away from the requested Place.

The focused marker alone uses a dedicated pane above public clusters and below navigation/user panes; release restores its original pane. This prevents a selected bin being covered by a nearby Place cluster without giving ordinary commercial markers extra priority.

The original `bins`/`managedPlaces` arrays remain authoritative. Nearest-bin selection still calls the existing server endpoint with the user coordinate; it never scans cluster centroids. Directions still use the original bin/Place coordinate. GPS watches, point submission, persistence, distance, proximity, Finish and routing budget/provider logic are unchanged.

## Accessibility

Each cluster is a focusable Leaflet marker with `role=button`, visible focus outline and a count-based Slovenian label, e.g. `12 lokacij na tem območju. Povečaj ali razpri skupino.` The visible count is aria-hidden to avoid repetition. Labels refresh whenever icons/counts are recreated. Leaflet's existing Enter support is retained; Space activates the same click behavior. Individual marker accessible names and trusted category SVGs remain unchanged. DOM text and CSS borders remain sharp at DPR 1 and 2.

## Verification and reproduction

Use synthetic captured Razor HTML, not production. Before the .NET tests, set `DOGGYDROP_POPUP_CAPTURE` and `DOGGYDROP_WALKING_CAPTURE` to an external directory. Configure `LEAFLET_TEST_ASSETS` with local Leaflet 1.9.4 JS/CSS and Bootstrap Icons 1.11.3 CSS/font, and make Playwright resolvable via the local Node environment. Set `HOME_CLUSTER_RESULTS` to an external output directory and optionally `BROWSER_CHANNEL` (default `msedge`). Run:

```
dotnet build
dotnet test
node --test DoggyDrop.Tests/JavaScript/*.test.cjs
node DoggyDrop.Tests/Browser/home-map-locations.cjs
```

The browser fixture runs the actual Home inline JS plus real Leaflet/plugin, intercepting every request with synthetic responses/local assets or aborting it. It never contacts production, ORS or live tile providers. CARTO uses a fake public fixture key. Images use synthetic tiles, so screenshots demonstrate marker density and UI, not actual cartographic content or today's production data.

It covers 7 widths (320,375,390,430,768,1024,1440), 7 scenes (national/regional/city/dense/street/mixed/logo), OSM/CARTO and DPR 1/2; counts, labels and centering; 250/500/1500/3000 synthetic markers; cutoff comparisons; filters/replacement; pinned logo/Featured/fallback; bin/Place deep links; nearest-bin invariance; bin/Place destination coordinates; keyboard spiderfy; Home active trail separation; and CARTO error fallback. Timings are observations, not fragile pass/fail millisecond thresholds. Member conservation counts offscreen members as stored members and selected markers separately; rendering outside the viewport is deliberately culled.

The existing icon/public-surface/gallery and attribution fixtures still isolate individual artwork/overlay layout; their small logical-layer adapters do not substitute for the real Home suite. Existing Node and .NET suites continue to cover GPS, privacy, Nearby, routing, authorisation and basemap behavior.

Future WaterPoint can create another logical `homeLocations.createLayer()` and populate/replace it with its own persisted public markers. Its toggle would enable/remove that layer, contributing to the same count. No WaterPoint data model, endpoint, marker artwork or UI is implemented here.

Physical-device follow-up remains: iOS Safari/Android touch tapping and pinch zoom, screen-reader announcement order with VoiceOver/TalkBack, and frame rate on a low-end phone. Desktop touch-size/DPR fixtures do not certify hardware performance.
