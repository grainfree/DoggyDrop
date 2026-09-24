const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const root = path.resolve(__dirname, "..", "..");
const read = relative => fs.readFileSync(path.join(root, relative), "utf8");

function navigationFunction(source, name) {
    const match = source.match(new RegExp(`function ${name}\\(type\\) \\{([^{}]+)\\}`));
    assert.ok(match, `${name} exists`);
    return new Function("type", match[1]);
}

test("navigation headings keep bins distinct from Places and generic destinations", () => {
    const map = read("DoggyDrop/Views/Map/Index.cshtml");
    const normalize = navigationFunction(map, "navigationDestinationType");
    const heading = navigationFunction(map, "navigationHeading");
    assert.equal(heading(normalize("bin")), "NAVIGACIJA DO KOŠA");
    assert.equal(heading(normalize("place")), "NAVIGACIJA DO LOKACIJE");
    assert.equal(heading(normalize("generic")), "NAVIGACIJA DO LOKACIJE");
    assert.equal(heading(normalize("untrusted")), "NAVIGACIJA DO LOKACIJE");
    assert.match(map, /navigateToPlaceInApp\(place\.latitude, place\.longitude, place\.name, "place"\)/);
    assert.match(map, /startInAppBinNavigation\(0, target, destinationType\)/);
    assert.match(map, /escapeHtml\(bin\.name \|\| bin\.Name \|\| "Cilj"\)/);
    assert.match(map, /name: name \|\| place\?\.name \|\| "Izbrana lokacija"/);
});

function runEditor(latitudeValue, longitudeValue) {
    const script = read("DoggyDrop/wwwroot/js/place-editor.js");
    const handlers = {};
    const views = [];
    const button = {
        disabled: false, textContent: "Shrani lokacijo", attributes: new Map(),
        setAttribute(name, value) { this.attributes.set(name, value); },
        removeAttribute(name) { this.attributes.delete(name); }
    };
    const form = {
        querySelector: () => button,
        addEventListener(name, handler) { handlers[name] = handler; }
    };
    const input = value => ({ value, addEventListener() {} });
    const latitude = input(latitudeValue);
    const longitude = input(longitudeValue);
    const map = {
        setView(point, zoom) { views.push([Array.from(point), zoom]); return this; },
        getZoom() { return 14; }, on() {}, invalidateSize() {}
    };
    const leaflet = {
        map: () => map,
        tileLayer: () => ({ addTo() {} }),
        circleMarker: () => ({ addTo() { return this; }, setLatLng() {} })
    };
    const window = { L: leaflet, addEventListener(name, handler) { handlers[name] = handler; } };
    const document = {
        querySelector: () => form,
        getElementById(id) {
            return ({ placePickerMap: {}, Latitude: latitude, Longitude: longitude,
                placePickerStatus: { textContent: "" } })[id] ?? null;
        }
    };
    vm.runInNewContext(script, { document, window, L: leaflet, requestAnimationFrame: callback => callback() });
    return { handlers, views, button };
}

test("Create starts at Home default, while Edit centers on stored Place", () => {
    const create = runEditor("", "");
    assert.deepEqual(create.views, [[[46.5547, 15.6459], 14]]);
    const edit = runEditor("46.2", "15.1");
    assert.deepEqual(edit.views, [
        [[46.5547, 15.6459], 14],
        [[46.2, 15.1], 16]
    ]);
});

test("admin form blocks a second valid submit and resets on page restoration", () => {
    for (const view of ["Create", "Edit"]) {
        const markup = read(`DoggyDrop/Views/AdminPlaces/${view}.cshtml`);
        assert.match(markup, /class="places-form"/);
        assert.match(markup, /button type="submit" class="places-button places-button--primary"/);
    }
    const { handlers, button } = runEditor("", "");
    handlers.submit({ preventDefault() { throw Error("first submit was prevented"); } });
    assert.equal(button.disabled, true);
    assert.equal(button.textContent, "Shranjujem ...");
    assert.equal(button.attributes.get("aria-busy"), "true");
    let prevented = false;
    handlers.submit({ preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    handlers.pageshow();
    assert.equal(button.disabled, false);
    assert.equal(button.textContent, "Shrani lokacijo");
});

test("Place image uses no-referrer and a failed image shows a fallback", () => {
    const details = read("DoggyDrop/Views/Places/Details.cshtml");
    assert.match(details, /referrerpolicy="no-referrer"/);
    assert.match(details, /places-details__media-fallback/);
    const listeners = {};
    const image = {
        hidden: false, complete: false, naturalWidth: 0,
        addEventListener(name, handler) { listeners[name] = handler; }
    };
    const fallback = { hidden: true };
    const document = {
        querySelector(selector) { return selector.endsWith("img") ? image : fallback; },
        getElementById() { return null; }
    };
    vm.runInNewContext(read("DoggyDrop/wwwroot/js/place-details.js"), { document, window: {} });
    listeners.error();
    assert.equal(image.hidden, true);
    assert.equal(fallback.hidden, false);

    image.hidden = false;
    image.complete = true;
    fallback.hidden = true;
    vm.runInNewContext(read("DoggyDrop/wwwroot/js/place-details.js"), { document, window: {} });
    assert.equal(image.hidden, true);
    assert.equal(fallback.hidden, false);
});
