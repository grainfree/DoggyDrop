const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path'), test = require('node:test'), vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
function load(provider) {
    const requests = [], credits = new Set();
    const container = { dataset: {}, classList: { add() {} } };
    const map = { getContainer: () => container, hasLayer: () => true,
        attributionControl: { addAttribution: a => credits.add(a), removeAttribution: a => credits.delete(a) } };
    const window = { L: { tileLayer(url, options) {
        const handlers = {};
        const layer = { url, options, changes: 0, addTo(m) { assert.equal(m, map); credits.add(options.attribution); return this; },
            on(name, fn) { handlers[name] = fn; return this; }, off(name) { delete handlers[name]; return this; },
            emit(name) { handlers[name]?.(); }, setUrl(value) { this.url = value; this.changes++; return this; } };
        requests.push(layer); return layer;
    } } };
    vm.runInNewContext(read('DoggyDrop/wwwroot/js/map-basemap.js'), { window, document: { currentScript: { dataset: { provider } } } });
    return { add: options => window.DoggyDropBasemap.addTo(map, options), requests, credits, container };
}
test('missing/invalid provider uses only canonical OSM without credentials', () => {
    for (const provider of [undefined, null, '', 'STADIA', 'https://evil.invalid', 'stadia?UserId=1', 'private-secret']) {
        const f = load(provider), [layer] = f.add();
        assert.equal(f.requests.length, 1);assert.equal(layer.url, 'https://tile.openstreetmap.org/{z}/{x}/{y}.png');
        assert.equal(layer.options.maxZoom, 20);assert.equal(layer.options.maxNativeZoom, 19);
        assert.match(layer.options.attribution, /href="https:\/\/www.openstreetmap.org\/copyright"/);
        assert.doesNotMatch(layer.url, /secret|UserId|api_key|key=/);
    }
});
test('enabled DoggyDrop style uses one EU Alidade Smooth raster layer with all attribution', () => {
    const f = load('stadia'), [layer] = f.add();assert.equal(f.requests.length, 1);
    assert.equal(layer.url, 'https://tiles-eu.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png');
    assert.equal(layer.options.maxNativeZoom, 20);
    for (const name of ['Stadia Maps', 'OpenMapTiles', 'OpenStreetMap']) assert.ok(layer.options.attribution.includes(name));
    assert.equal(layer.options.detectRetina, false);assert.equal(layer.options.referrerPolicy, 'strict-origin-when-cross-origin');
    assert.equal(f.container.dataset.basemap, 'stadia');
});
test('existing per-surface maximum zoom is retained without altering the camera', () => {
    for (const provider of ['stadia','osm']) {
        assert.equal(load(provider).add({maxZoom:19})[0].options.maxZoom,19);
        assert.equal(load(provider).add({maxZoom:20})[0].options.maxZoom,20);
    }
});
test('three tile failures fall back once on the same layer and update attribution/native zoom', () => {
    const f = load('stadia'), [layer] = f.add({maxZoom:19});
    layer.emit('tileerror');layer.emit('tileerror');assert.equal(layer.changes,0);
    layer.emit('tileerror');assert.match(layer.url,/^https:\/\/tile.openstreetmap.org/);
    assert.equal(layer.options.maxNativeZoom,19);assert.equal(layer.options.maxZoom,19);assert.equal(layer.changes,1);
    assert.equal(f.container.dataset.basemap,'osm');assert.equal(f.credits.size,1);
    assert.doesNotMatch([...f.credits][0],/Stadia|OpenMapTiles/);
    for(let i=0;i<20;i++)layer.emit('tileerror');assert.equal(layer.changes,1);assert.equal(f.requests.length,1);
});
test('OSM tile failure cannot create another provider or retry loop', () => {
    const f=load(),[layer]=f.add();for(let i=0;i<20;i++)layer.emit('tileerror');assert.equal(layer.changes,0);assert.equal(f.requests.length,1);
});
test('each Leaflet view uses the same guarded configuration and stylesheet partials', () => {
    const views=['Map/Index','Walks/Active','Walks/Details','Walks/Planner','Places/Details','AdminPlaces/Create','AdminPlaces/Edit','Map/Add','Home/Index'];
    for(const view of views){const source=read('DoggyDrop/Views/'+view+'.cshtml');
        assert.match(source,/<partial name="_MapBasemapScripts"/);assert.match(source,/<partial name="_MapBasemapStyles"/);
        assert.doesNotMatch(source,/L\.tileLayer\(|CartoBasemap|cartoBasemapKey/);
    }
    assert.match(read('DoggyDrop/Views/Shared/_MapBasemapScripts.cshtml'),/Configuration\["Basemap:Provider"\] == "stadia" \? "stadia" : "osm"/);
    assert.match(read('DoggyDrop/Views/Shared/_MapBasemapStyles.cshtml'),/sha256-p4NxAoJBhIIN\+hmNHrzRCf9tD\/miZyoHS5obTRR9BMY=/);
    for(const name of ['place-details','place-editor'])assert.match(read('DoggyDrop/wwwroot/js/'+name+'.js'),/DoggyDropBasemap.addTo\(map/);
});
test('Home/Active no longer recolor raster tiles and attribution UI remains readable', () => {
    for(const view of ['Map/Index','Walks/Active'])assert.doesNotMatch(read('DoggyDrop/Views/'+view+'.cshtml'),/filter: saturate/);
    const css=read('DoggyDrop/wwwroot/css/map-basemap.css');assert.match(css,/font-size: 11px/);assert.match(css,/:focus-visible/);assert.doesNotMatch(css,/filter:/);
});
test('route details diagnostics remain attached to the shared layer and persisted route fitting remains', () => {
    const details=read('DoggyDrop/Views/Walks/Details.cshtml');assert.match(details,/const tiles = DoggyDropBasemap.addTo\(map, \{ maxZoom: 19 \}\)\[0\]/);
    assert.match(details,/tiles.on\("load"/);assert.match(details,/L.polyline\(routePoints/);assert.match(details,/fitBounds\(/);
    assert.match(read('DoggyDrop/Views/Map/Index.cshtml'),/managedPlaceLayer = buildManagedPlaceLayer\(managedPlaces\).addTo\(map\)/);
});
test('Admin picker and Place Details keep tiles inside their map containers', () => {
    const css=read('DoggyDrop/wwwroot/css/places.css');
    assert.match(read('DoggyDrop/Views/AdminPlaces/_Form.cshtml'),/places-picker-map places-leaflet-map/);
    assert.match(read('DoggyDrop/Views/Places/Details.cshtml'),/places-details__map places-leaflet-map/);
    assert.match(css,/\.places-leaflet-map \{[^}]*position: relative;[^}]*overflow: hidden;/);
});
