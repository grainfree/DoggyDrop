const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const root = path.resolve(__dirname, "../..");
const read = file => fs.readFileSync(path.join(root, file), "utf8");
const L = { divIcon: options => options };

function load(file, name) {
    const window = { L };
    vm.runInNewContext(read(file), { window, URL });
    return window[name];
}

const bins = load("DoggyDrop/wwwroot/js/bin-marker.js", "DoggyDropBinMarker");
const places = load("DoggyDrop/wwwroot/js/place-marker.js", "DoggyDropPlaceMarker");

test("shared bin is 32px visually with a 44px touch box and no default badge", () => {
    const normal = bins.createIcon({ status: "ok" });
    const selected = bins.createIcon({ status: "ok" }, { selected: true });
    const full = bins.createIcon({ status: "full" });
    const missing = bins.createIcon({ status: "missing" });
    const css = read("DoggyDrop/wwwroot/css/bin-marker.css");
    assert.equal(Number(normal.iconSize[0]), 44);
    assert.equal(Number(normal.iconSize[1]), 48);
    assert.deepEqual(Array.from(normal.iconAnchor), [22, 43]);
    assert.deepEqual(Array.from(normal.popupAnchor), [0, -38]);
    assert.match(css, /\.map-bin-marker\s*\{[^}]*width: 32px;[^}]*height: 32px;[^}]*margin: 6px;/);
    assert.match(css, /\.map-bin-marker__status \{ display: none; \}/);
    assert.match(normal.html, /map-bin-marker__icon/);
    assert.match(normal.html, /class="map-bin-marker__lid" d="M7 10h18v3H7z"/);
    assert.match(normal.html, /class="map-bin-marker__body" d="M9 14h14l-1\.5 12h-11z"/);
    assert.doesNotMatch(normal.html, /<text\b|<image\b|bi-trash|>T<\/i>/i);
    assert.match(css, /\.map-bin-marker__icon \{ width: 23px; height: 23px; \}/);
    assert.match(css, /\.map-bin-marker__lid,[\s\S]*?\.map-bin-marker__body,[\s\S]*?\.map-bin-marker__handle \{\s*fill: currentColor;/);
    assert.match(css, /\.map-bin-marker__line \{[^}]*stroke: var\(--bin-fill\)/);
    assert.doesNotMatch(normal.html, /map-bin-marker--selected/);
    assert.match(selected.html, /map-bin-marker--selected/);
    assert.match(full.html, /map-bin-marker--full/);
    assert.match(missing.html, /map-bin-marker--missing/);
    assert.match(css, /\.map-bin-marker--full \.map-bin-marker__status,[\s\S]*?width: 8px;[^}]*height: 8px;/);
    assert.doesNotMatch(css, /14px 26px|rotate\(-45deg\)/);
});

test("Places use a 48px branded marker, safe contained logos, and distinct fallbacks", () => {
    const vet = places.createIcon({ category: 1, logoUrl: null });
    const shop = places.createIcon({ category: 2, logoUrl: "https://example.com/logo.png?x=1&y=2" });
    const generic = places.createIcon({ category: 99 });
    const css = read("DoggyDrop/wwwroot/css/places.css");
    assert.equal(Number(shop.iconSize[0]), 52);
    assert.ok(Number(shop.iconSize[0]) > Number(bins.createIcon({}).iconSize[0]));
    assert.match(css, /\.managed-place-pin \{[^}]*width: 48px;[^}]*height: 48px;/);
    assert.match(css, /\.managed-place-pin__image \{[^}]*inset: 2px;[^}]*width: calc\(100% - 4px\);[^}]*padding: 0;[^}]*object-fit: contain;/);
    assert.doesNotMatch(css, /\.managed-place-pin__image \{[^}]*object-fit: cover;/);
    assert.match(css, /\.managed-place-pin--selected \{[^}]*transform: scale\(1\.12\);/);
    assert.ok(Math.abs(48 * 1.12 - 54) < 1);
    assert.match(shop.html, /src="https:\/\/example\.com\/logo\.png\?x=1&amp;y=2"/);
    assert.match(shop.html, /referrerpolicy="no-referrer"/);
    assert.match(shop.html, /bi-bag-fill/);
    assert.match(vet.html, /bi-heart-pulse-fill/);
    assert.doesNotMatch(vet.html, /<img/);
    assert.match(generic.html, /bi-geo-alt-fill/);
    const selectedShop = places.createIcon({ category: 2, logoUrl: "https://example.com/logo.png" }, { selected: true });
    assert.match(selectedShop.html, /managed-place-pin--selected/);
    assert.match(selectedShop.html, /src="https:\/\/example\.com\/logo\.png"/);
    const separatePhoto = places.createIcon({ category: 2, logoUrl: "https://example.com/logo.png", imageUrl: "https://example.com/photo.jpg" });
    assert.match(separatePhoto.html, /logo\.png/);
    assert.doesNotMatch(separatePhoto.html, /photo\.jpg/);
    assert.doesNotMatch(places.createIcon({ category: 2, imageUrl: "https://example.com/photo.jpg" }).html, /<img/);
    assert.equal(vet.iconSize[0], shop.iconSize[0]);
});

test("unsafe or missing logos never create an image request or inject markup", () => {
    for (const url of [null, "", "http://example.com/logo.png", "javascript:alert(1)",
        "https://user:password@example.com/logo.png", "https://example.com/a b.png", "https://example.com/\\evil.png"]) {
        assert.equal(places.safeImageUrl(url), null);
        assert.doesNotMatch(places.createIcon({ category: 1, logoUrl: url }).html, /<img/);
    }
    const injected = places.createIcon({ category: 2, logoUrl: 'https://example.com/logo.png?q="evil"' }).html;
    assert.doesNotMatch(injected, /onerror="alert/);
    assert.match(injected, /&quot;/);
    assert.doesNotMatch(injected, /<script/);
    assert.doesNotMatch(read("DoggyDrop/wwwroot/js/place-marker.js"), /fetch\(|XMLHttpRequest/);
});

test("map layers keep branded Places independent and above bins, below the user", () => {
    const map = read("DoggyDrop/Views/Map/Index.cshtml");
    const active = read("DoggyDrop/Views/Walks/Active.cshtml");
    for (const [pane, z] of [["binMarkers", 590], ["placeMarkers", 610], ["navigationMarkers", 620], ["userMarkers", 630]]) {
        assert.ok(map.includes(`map.createPane("${pane}").style.zIndex = ${z}`));
    }
    assert.match(map, /managedPlaceLayer = buildManagedPlaceLayer\(managedPlaces\)\.addTo\(map\)/);
    assert.match(map, /L\.marker\(\[lat, lng\], \{ icon: DoggyDropPlaceMarker\.createIcon\(place\), placeId: id, pane: "placeMarkers" \}\)/);
    assert.match(map, /L\.marker\(\[bin\.latitude, bin\.longitude\], \{ icon: createBinIcon\(bin\), pane: "binMarkers" \}\)/);
    assert.match(map, /navigation\.previousLayers = \[binLayer, managedPlaceLayer,/);
    assert.match(map, /navigation\.previousLayers\.forEach\(layer => layer\.addTo\(map\)\)/);
    assert.match(map, /navigation\.destinationType === "bin"\s*\? createBinIcon\(bin, \{ selected: true \}\)/);
    assert.match(map, /navigation\.destinationType === "place"\s*\? DoggyDropPlaceMarker\.createIcon\(bin, \{ selected: true \}\)/);
    assert.match(active, /DoggyDropBinMarker\.createIcon\(bin\)/);
    assert.doesNotMatch(map, /new L\.Icon\.Default|L\.Icon\.Default/);
});

test("Place Details uses the same marker and already-projected safe image URL", () => {
    const map = read("DoggyDrop/Views/Map/Index.cshtml");
    const details = read("DoggyDrop/Views/Places/Details.cshtml");
    const script = read("DoggyDrop/wwwroot/js/place-details.js");
    assert.match(map, /<script src="~\/js\/place-marker\.js"/);
    assert.match(details, /<script src="~\/js\/place-marker\.js"/);
    assert.match(details, /data-logo-url="@Model\.LogoUrl"/);
    assert.match(script, /DoggyDropPlaceMarker\.createIcon/);
    assert.match(script, /DoggyDropPlaceMarker\.attachImage/);
    assert.match(read("DoggyDrop/Controllers/MapController.cs"), /place with \{ LogoUrl = PlaceLogoDelivery\.ForMarker\(place\.LogoUrl, _placeLogoCloud\.Value\) \}/);
    assert.doesNotMatch(script, /fetch\(|XMLHttpRequest/);
});
