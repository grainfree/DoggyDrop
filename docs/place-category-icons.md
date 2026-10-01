# Place category icons (Epic 19.7)

`PlaceCategories` in `Services/PlacePresentation.cs` is the authoritative enum-to-icon-class mapping. Application-owned SVG masks are bundled in `wwwroot/css/place-category-icons.css`, loaded once with a versioned URL by `_Layout`. No icon library or per-marker network request is added. Each drawing uses a 24×24 viewBox, 1.9-unit rounded strokes and dark green. Existing marker sizes, category borders, anchors, Featured rings and selected scaling remain.

| Value / category | Previous Bootstrap symbol | Current symbol | Managed business logo |
|---|---|---|---|
| 1 Veterinarian | Heartbeat | Dog face + medical cross | Preferred |
| 2 PetShop | Shopping bag | Shopping bag | Preferred |
| 3 Groomer | Scissors | Scissors | Preferred |
| 4 DogSchool | Graduation cap | Dog profile + check | Preferred |
| 5 DogFriendlyCafe | Hot cup | Hot cup | Preferred |
| 6 DogPark | Tree | Dog face | Suppressed, as before |
| 7 DogBeach | Water | Dog profile + waves | Suppressed, as before |

Unknown/invalid presentation uses the bundled location pin. Adding an enum value without a presentation/drawing fails permanent .NET/Node coverage. No raw SVG is added to public DTOs: the existing `iconClass` field now selects local artwork. The server never derives it from user/database markup. Clients accept only the scoped class syntax; SVG contains only application-owned geometry in CSS data URLs. Place text remains encoded/textContent, and existing safe managed-logo projection remains.

Home markers and popups share `DoggyDropPlaceMarker.iconClass`. Discovery and Saved use `_PlaceCard`; Details uses the same class for destination, photo fallback and map. Commercial Details remains text-only when no logo/photo is present; a broken existing logo now reveals the category icon within its existing box. Admin has textual category selection and no category preview to change. All logo-capable categories retain valid managed logo > category icon, including cached-load/error fallback. Featured/selected behavior and Saved ordering are unchanged.

Epic 19.8 retires the legacy Home park/water/café layers from current discovery. Current Places retain this icon system unchanged. The separate water-only mask remains available as artwork, but does not represent a current supported Place category or public WaterPoint. See `legacy-poi-retirement.md` for historical compatibility.

Icons are decorative (`aria-hidden` directly or through their media container). Category text remains visible on cards/popups/Details, and Home marker image labels include the escaped Place name and category. Leaflet keyboard/click/popup behavior is preserved. No raw SVG DOM or external icon asset is accepted.

## Offline verification

Run `dotnet build`, `dotnet test`, and `node --test DoggyDrop.Tests/JavaScript/*.test.cjs`. The category HTTP test exercises all enum values in rendered Home, Discovery, Saved and Details; Node tests cover enum/CSS completeness, distinct safe SVGs, no letter/tree fallback, logos (including cached/error states), hostile text, accessibility and Featured/selected geometry.

For reproducible browser fixtures, set `DOGGYDROP_POPUP_CAPTURE` and `DOGGYDROP_WALKING_CAPTURE` to an external temporary directory before `dotnet test`. Set `LEAFLET_TEST_ASSETS` to local Leaflet 1.9.4 and Bootstrap Icons 1.11.3 files as described in `map-basemap.md`, `PLACE_ICON_RESULTS` to an external output directory, and optionally `BROWSER_CHANNEL` (default `msedge`). Make Playwright available through the local Node environment, then run:

```text
node DoggyDrop.Tests/Browser/place-category-icons.cjs
```

The fixture uses rendered Home markup, the actual Home marker/popup builder and actual public-page stylesheet links. It checks 11 marker variants × six widths (320, 375, 390, 430, 1024, 1440) × two pixel densities, plus Discovery/Saved/seven Details surfaces at six widths. Real keyboard popup opening and directions callbacks are exercised. Logo and pale map tiles are synthetic; every network request is fulfilled locally or aborted. No provider keys, real CARTO tiles or production records are used. Pasje igrišče Vir is a synthetic name/coordinate fixture, not a production read. The existing `map-attribution.cjs` provides the broader 96-scene layout regression.

Physical follow-up: confirm icon recognition and mask rendering on iOS Safari/Android Chrome, outdoor contrast, enlarged text, touch and VoiceOver/TalkBack. Desktop fixtures at 1×/2× do not replace those checks. No category/data/importer/auth/SEO/privacy/basemap/routing/GPS/schema change is included.

Verification on 29 September 2026: build and explicit Razor rebuild PASS;
1,160/1,160 .NET, 135/135 Node, 198/198 icon/public-surface browser cases
(including 12 actual Details logo success/failure cases), and 96/96 existing
map-layout cases PASS. The final gallery verifies 264 normal/selected marker
renders across six widths and two pixel densities. All 26 standalone/inline JS syntax checks and Git
whitespace checks PASS. Existing NU1902 MailKit/MimeKit warnings remain outside
this presentation change. No new BLOCKER/HIGH/MEDIUM/LOW findings remain.


## Final owner-approved selection

Veterinarian C (dog face + medical cross) and DogSchool A (dog profile + check)
are retained. DogBeach uses the exact prototyped C artwork (dog profile + two
waves), replacing B (dog face + one wave). DogPark keeps the accepted dog face.
The four categories differ by shape/cue, not just colour. PetShop, Groomer and
DogFriendlyCafe are unchanged. Permanent geometry snapshots explicitly protect
all four owner-approved dog-related symbols alongside existing coverage.

The final browser gallery contains all seven categories, valid/broken Mr.Pet
fixture logos, Featured and selected examples, with labelled normal/selected
markers. It ships no rejected alternatives. Final local verification artifacts
are under `C:/Codex/epic197-final/`. Earlier alternatives are external historical
review artifacts only. Legacy generic water retains the original waves-only
mask; no WaterPoint or other category is introduced.
