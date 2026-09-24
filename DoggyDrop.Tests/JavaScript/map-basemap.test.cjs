const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const root = path.resolve(__dirname, "..", "..");
const read = relative => fs.readFileSync(path.join(root, relative), "utf8");

function loadBasemap() {
    const requests = [];
    const window = { L: { tileLayer(url, options) {
        requests.push({ url, options });
        return { addTo(map) { assert.ok(map); return this; } };
    } } };
    vm.runInNewContext(read("DoggyDrop/wwwroot/js/map-basemap.js"), { window, encodeURIComponent });
    return { addTo: window.DoggyDropBasemap.addTo, requests };
}

test("missing public CARTO key initializes only the OSM basemap", () => {
    for (const key of [null, "", "   "]) {
        const { addTo, requests } = loadBasemap();
        const layers = addTo({}, key, true);
        assert.equal(layers.length, 1);
        assert.equal(requests.length, 1);
        assert.match(requests[0].url, /^https:\/\/\{s\}\.tile\.openstreetmap\.org\//);
        assert.doesNotMatch(requests[0].url, /cartocdn|crowdsource/);
        assert.match(requests[0].options.attribution, /OpenStreetMap/);
    }
});

test("configured public key preserves CARTO base and optional labels", () => {
    const { addTo, requests } = loadBasemap();
    addTo({}, "public key", true);
    assert.equal(requests.length, 2);
    assert.match(requests[0].url, /voyager_nolabels/);
    assert.match(requests[1].url, /voyager_only_labels/);
    for (const request of requests) {
        assert.match(request.url, /\?key=public%20key$/);
        assert.match(request.options.attribution, /CARTO/);
    }
});

test("Home and Planner use the guarded basemap and Places remain initialized", () => {
    for (const view of ["DoggyDrop/Views/Map/Index.cshtml", "DoggyDrop/Views/Walks/Planner.cshtml"]) {
        const source = read(view);
        assert.match(source, /map-basemap\.js/);
        assert.match(source, /Configuration\["CartoBasemap:PublicApiKey"\]/);
        assert.match(source, /DoggyDropBasemap\.addTo\(map, cartoBasemapKey/);
        assert.doesNotMatch(source, /L\.tileLayer\("https:\/\/\{s\}\.basemaps\.cartocdn\.com/);
    }
    assert.match(read("DoggyDrop/Views/Map/Index.cshtml"), /managedPlaceLayer = buildManagedPlaceLayer\(managedPlaces\)\.addTo\(map\)/);
});

test("Admin picker keeps Leaflet tiles inside the map container", () => {
    const css = read("DoggyDrop/wwwroot/css/places.css");
    assert.match(css, /\.places-picker-map \{[^}]*position: relative;[^}]*overflow: hidden;/);
    assert.match(css, /\.places-picker-map \.leaflet-tile-container[^}]*position: absolute;/);
    assert.match(css, /\.places-picker-map \.leaflet-tile \{ width: 256px; height: 256px; \}/);
});
