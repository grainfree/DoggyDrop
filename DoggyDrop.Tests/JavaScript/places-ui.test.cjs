const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const root = path.resolve(__dirname, "..", "..");
const read = relative => fs.readFileSync(path.join(root, relative), "utf8");

function loadPlaceMarker(L) {
    const window = { L };
    vm.runInNewContext(read("DoggyDrop/wwwroot/js/place-marker.js"), { window, URL });
    return window.DoggyDropPlaceMarker;
}

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
    assert.ok(map.includes('navigateToPlaceInApp(place.latitude, place.longitude, place.name, "place", place)'));
    assert.match(map, /startInAppBinNavigation\(0, target, destinationType\)/);
    assert.match(map, /escapeHtml\(bin\.name \|\| bin\.Name \|\| "Cilj"\)/);
    assert.match(map, /name: name \|\| place\?\.name \|\| "Izbrana lokacija"/);
});

function runEditor(latitudeValue, longitudeValue) {
    const script = read("DoggyDrop/wwwroot/js/place-editor.js");
    const handlers = {};
    const views = [];
    const observations = [];
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
        getZoom() { return 14; }, on() {}, invalidateSize(options) { observations.push(options ?? null); }
    };
    const leaflet = {
        map: () => map,
        tileLayer: () => ({ addTo() {} }),
        circleMarker: () => ({ addTo() { return this; }, setLatLng() {} })
    };
    const window = {
        L: leaflet,
        ResizeObserver: class { constructor(callback) { handlers.resize = callback; } observe() {} },
        addEventListener(name, handler) { handlers[name] = handler; }
    };
    const document = {
        querySelector: () => form,
        getElementById(id) {
            return ({ placePickerMap: {}, Latitude: latitude, Longitude: longitude,
                placePickerStatus: { textContent: "" } })[id] ?? null;
        }
    };
    vm.runInNewContext(script, { document, window, L: leaflet, requestAnimationFrame: callback => callback() });
    return { handlers, views, observations, button };
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

test("Admin picker recalculates Leaflet size when its container changes", () => {
    const { handlers, observations } = runEditor("", "");
    assert.equal(observations.length, 1);
    handlers.resize();
    assert.equal(observations.length, 2);
    assert.equal(observations[1].pan, false);
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

test("Home Place markers and popups show safe logos without losing category fallback", () => {
    const map = read("DoggyDrop/Views/Map/Index.cshtml");
    const buildStart = map.indexOf("        function buildManagedPlaceLayer(places) {");
    const buildEnd = map.indexOf("        function buildPlaceLayer(places) {", buildStart);
    const escapeStart = map.indexOf("        function escapeHtml(value) {");
    const escapeEnd = map.indexOf("    </script>", escapeStart);
    assert.ok(buildStart > 0 && buildEnd > buildStart && escapeStart > 0 && escapeEnd > escapeStart);

    let iconCreations = 0;
    const classList = () => {
        const values = new Set();
        return {
            add: value => values.add(value),
            remove: value => values.delete(value),
            contains: value => values.has(value)
        };
    };
    const L = {
        layerGroup: () => ({ markers: [] }),
        divIcon: options => { iconCreations++; return options; },
        marker: (coordinates, options) => {
            const pin = { classList: classList() };
            const src = options.icon.html.match(/src="([^"]+)"/)?.[1];
            const image = src ? {
                src, hidden: false, complete: true, naturalWidth: 40, dataset: {}, parentElement: pin,
                listeners: {}, addEventListener(event, handler) { this.listeners[event] = handler; }
            } : null;
            const element = {
                pin, image,
                querySelector(selector) {
                    return selector === ".managed-place-pin" ? pin
                        : selector === ".managed-place-image" ? image : null;
                }
            };
            return {
                coordinates, options, element, handlers: {},
                bindPopup(html) { this.popup = html; return this; },
                on(event, handler) { this.handlers[event] = handler; return this; },
                setIcon() { throw new Error("Popup selection must not rebuild the icon"); },
                setZIndexOffset(value) { this.zIndexOffset = value; return this; },
                getElement() { return this.element; },
                getPopup() { return null; },
                addTo(layer) { layer.markers.push(this); return this; }
            };
        }
    };
    const placeMarker = loadPlaceMarker(L);
    const build = new Function("L", "attachManagedPlaceImage", "DoggyDropPlaceMarker",
        `${map.slice(escapeStart, escapeEnd)}\n${map.slice(buildStart, buildEnd)}\nreturn buildManagedPlaceLayer;`)(L, placeMarker.attachImage, placeMarker);
    const withLogo = build([{
        id: 1, name: "Mr.<Pet>", address: "<Unsafe> street", category: 2,
        latitude: 46.1, longitude: 15.1, imageUrl: "https://example.com/logo.png?x=1&y=2"
    }]).markers[0];
    assert.match(withLogo.options.icon.html, /managed-place-pin--pet-shop/);
    assert.match(withLogo.options.icon.html, /bi-bag-fill/);
    assert.match(withLogo.options.icon.html, /src="https:\/\/example\.com\/logo\.png\?x=1&amp;y=2"/);
    assert.equal((withLogo.options.icon.html.match(/referrerpolicy="no-referrer"/g) || []).length, 1);
    assert.match(withLogo.popup, /managed-place-popup__media/);
    assert.match(withLogo.popup, /referrerpolicy="no-referrer"/);
    assert.doesNotMatch(withLogo.popup, /loading="lazy"/);
    assert.match(withLogo.popup, /Mr\.&lt;Pet&gt;/);
    assert.match(withLogo.popup, /&lt;Unsafe&gt; street/);
    assert.doesNotMatch(withLogo.popup, /<Unsafe>/);
    assert.equal(typeof withLogo.handlers.add, "function");
    assert.equal(typeof withLogo.handlers.popupopen, "function");
    const originalIcon = withLogo.options.icon;
    const originalElement = withLogo.getElement();
    const originalImage = originalElement.image;
    const originalSrc = originalImage.src;
    const initialIconCreations = iconCreations;
    withLogo.handlers.add();
    assert.equal(originalElement.pin.classList.contains("has-image"), true);
    withLogo.handlers.popupopen();
    assert.equal(originalElement.pin.classList.contains("managed-place-pin--selected"), true);
    withLogo.handlers.popupclose();
    assert.equal(originalElement.pin.classList.contains("managed-place-pin--selected"), false);
    withLogo.handlers.popupopen();
    assert.equal(withLogo.options.icon, originalIcon);
    assert.equal(withLogo.getElement(), originalElement);
    assert.equal(withLogo.getElement().image, originalImage);
    assert.equal(originalImage.src, originalSrc);
    assert.equal(iconCreations, initialIconCreations);
    withLogo.handlers.popupclose();

    for (const imageUrl of [null, "javascript:alert(1)"]) {
        const fallback = build([{
            id: 2, name: "Vet", category: 1, latitude: 46.1, longitude: 15.1, imageUrl
        }]).markers[0];
        assert.match(fallback.options.icon.html, /bi-heart-pulse-fill/);
        assert.doesNotMatch(fallback.options.icon.html, /<img/);
        assert.doesNotMatch(fallback.popup, /managed-place-popup__media/);
        fallback.handlers.popupopen();
        assert.equal(fallback.element.pin.classList.contains("managed-place-pin--selected"), true);
        fallback.handlers.popupclose();
        assert.equal(fallback.element.pin.classList.contains("managed-place-pin--selected"), false);
    }

    const broken = build([{
        id: 3, name: "Broken", category: 2, latitude: 46.1, longitude: 15.1,
        imageUrl: "https://example.com/broken.png"
    }]).markers[0];
    broken.element.image.naturalWidth = 0;
    broken.handlers.add();
    const brokenImage = broken.element.image;
    assert.equal(brokenImage.hidden, true);
    broken.handlers.popupopen();
    broken.handlers.popupclose();
    assert.equal(broken.element.image, brokenImage);
    assert.equal(brokenImage.hidden, true);
    assert.equal(broken.element.pin.classList.contains("has-image"), false);

    const other = build([{
        id: 4, name: "Other", category: 1, latitude: 46.2, longitude: 15.2,
        imageUrl: "https://example.com/other.png"
    }]).markers[0];
    const otherImage = other.element.image;
    withLogo.handlers.popupopen();
    withLogo.handlers.popupclose();
    other.handlers.popupopen();
    assert.equal(withLogo.element.pin.classList.contains("managed-place-pin--selected"), false);
    assert.equal(other.element.pin.classList.contains("managed-place-pin--selected"), true);
    assert.equal(withLogo.element.image, originalImage);
    assert.equal(other.element.image, otherImage);
    other.handlers.popupclose();
    other.handlers.add();
    other.handlers.popupopen();
    assert.equal(other.element.pin.classList.contains("managed-place-pin--selected"), true);
    assert.equal(other.element.image, otherImage);
    other.element = null;
    assert.doesNotThrow(() => {
        other.handlers.popupclose();
        other.handlers.popupopen();
    });
});

test("Home Place image load and failure retain the category icon", () => {
    const attach = loadPlaceMarker({ divIcon: options => options }).attachImage;
    const image = (complete, naturalWidth) => {
        const classes = new Set();
        const listeners = {};
        const target = {
            complete, naturalWidth, hidden: false, dataset: {}, listeners,
            parentElement: { classList: { add: name => classes.add(name), remove: name => classes.delete(name) } },
            addEventListener(name, callback) { listeners[name] = callback; }
        };
        attach({ querySelector: () => target });
        return { target, classes, listeners };
    };
    const loaded = image(true, 80);
    assert.equal(loaded.classes.has("has-image"), true);
    const failed = image(false, 0);
    failed.listeners.error();
    assert.equal(failed.target.hidden, true);
    assert.equal(failed.classes.has("has-image"), false);
    const cachedFailure = image(true, 0);
    assert.equal(cachedFailure.target.hidden, true);
    const css = read("DoggyDrop/wwwroot/css/places.css");
    assert.match(css, /\.managed-place-pin\.has-image \.managed-place-pin__image/);
    assert.match(css, /\.managed-place-popup__media\.has-image/);
});

function runDetailsMap(category, latitude = "46.05", cartoKey = "", imageUrl = "") {
    const observations = { maps: 0, layers: 0, markers: [], sizes: [], center: null, key: null, resize: null };
    const element = { dataset: { latitude, longitude: "14.51", category, imageUrl, cartoBasemapKey: cartoKey } };
    const map = {
        setView(center) { observations.center = Array.from(center); return this; },
        invalidateSize(options) { observations.sizes.push(options); }
    };
    const L = {
        map: () => { observations.maps++; return map; },
        divIcon: options => options,
        marker: (position, options) => ({ addTo() { observations.markers.push({ position, options }); }, getElement() { return null; } })
    };
    const window = {
        L,
        DoggyDropPlaceMarker: loadPlaceMarker(L),
        DoggyDropBasemap: { addTo(target, key) { assert.equal(target, map); observations.layers++; observations.key = key; } },
        ResizeObserver: class { constructor(callback) { observations.resize = callback; } observe(target) { assert.equal(target, element); } }
    };
    const document = {
        querySelector: () => null,
        getElementById: id => id === "placeDetailsMap" ? element : null
    };
    vm.runInNewContext(read("DoggyDrop/wwwroot/js/place-details.js"),
        { document, window, L, requestAnimationFrame: callback => callback() });
    return observations;
}

test("Place Details map initializes once with guarded basemap and category marker", () => {
    for (const [category, iconClass] of [["1", "bi-heart-pulse-fill"], ["2", "bi-bag-fill"]]) {
        const result = runDetailsMap(category);
        assert.equal(result.maps, 1);
        assert.equal(result.layers, 1);
        assert.equal(result.key, "");
        assert.equal(result.markers.length, 1);
        assert.match(result.markers[0].options.icon.html, new RegExp(iconClass));
        assert.deepEqual(result.center, [46.05, 14.51]);
        assert.equal(result.sizes.length, 1);
        result.resize();
        assert.equal(result.sizes.length, 2);
        assert.equal(result.sizes[1].pan, false);
    }
    assert.equal(runDetailsMap("2", "NaN").maps, 0);
    assert.equal(runDetailsMap("2", "46.05", "public-test-key").key, "public-test-key");
    assert.match(runDetailsMap("2", "46.05", "", "https://example.com/shop.png").markers[0].options.icon.html,
        /src="https:\/\/example\.com\/shop\.png"/);
    assert.doesNotMatch(read("DoggyDrop/wwwroot/js/place-details.js"), /basemaps\.cartocdn\.com|L\.tileLayer\(/);
});

test("Admin and Details share scoped Leaflet structure; Details media is contained", () => {
    const css = read("DoggyDrop/wwwroot/css/places.css");
    const admin = read("DoggyDrop/Views/AdminPlaces/_Form.cshtml");
    const details = read("DoggyDrop/Views/Places/Details.cshtml");
    const home = read("DoggyDrop/Views/Map/Index.cshtml");
    assert.match(admin, /class="places-picker-map places-leaflet-map"/);
    assert.match(details, /class="places-details__map places-leaflet-map"/);
    assert.match(css, /\.places-leaflet-map \{[^}]*overflow: hidden;/);
    assert.match(css, /\.places-leaflet-map \.leaflet-pane,/);
    assert.match(css, /\.places-leaflet-map \.leaflet-tile \{ width: 256px; height: 256px;/);
    assert.match(css, /\.places-picker-map \{ height: 320px; \}/);
    assert.match(css, /\.places-details__map \{ height: 300px; \}/);
    assert.match(css, /\.places-details__map \{ height: 260px; \}/);
    assert.match(details, /leaflet@1\.9\.4\/dist\/leaflet\.css/);
    assert.match(details, /~\/js\/map-basemap\.js/);
    assert.match(home, /~\/css\/places\.css/);
    assert.match(css, /\.places-details__media \{[^}]*width: min\(100%, 560px\);[^}]*240px\)/);
    assert.match(css, /\.places-details__media img \{[^}]*object-fit: contain;/);
    assert.match(details, /referrerpolicy="no-referrer"/);
    assert.match(details, /places-details__media-fallback/);
});
