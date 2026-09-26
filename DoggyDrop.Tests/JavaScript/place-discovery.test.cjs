const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const root = path.resolve(__dirname, "../..");
const read = file => fs.readFileSync(path.join(root, file), "utf8");
const source = read("DoggyDrop/wwwroot/js/place-discovery.js");
const window = {};
vm.runInNewContext(source, { window, document: { getElementById: () => null } });
const discovery = window.DoggyDropPlaceDiscovery;
const origin = { latitude: 46, longitude: 15, accuracy: 20 };

test("discovery location validates coordinates and rejects very poor accuracy", () => {
    assert.ok(discovery.validLocation(origin));
    for (const candidate of [null, { ...origin, latitude: NaN }, { ...origin, longitude: 181 },
        { ...origin, accuracy: Infinity }, { ...origin, accuracy: 3000 }]) {
        assert.ok(!discovery.validLocation(candidate));
    }
    assert.equal(discovery.distanceMeters(origin, { latitude: NaN, longitude: 15 }), null);
});

test("discovery distance uses metres and Slovenian decimal commas", () => {
    assert.equal(discovery.distanceMeters(origin, origin), 0);
    assert.ok(Math.abs(discovery.distanceMeters(origin, { latitude: 46.001, longitude: 15 }) - 111) < 2);
    for (const distance of [0.1, 0.49, 0.9])
        assert.equal(discovery.formatDistance(distance), "< 1 m");
    assert.equal(discovery.formatDistance(0), "0 m");
    assert.equal(discovery.formatDistance(1), "1 m");
    assert.equal(discovery.formatDistance(350), "350 m");
    assert.equal(discovery.formatDistance(999), "999 m");
    assert.equal(discovery.formatDistance(1000), "1,0 km");
    assert.equal(discovery.formatDistance(1200), "1,2 km");
    assert.equal(discovery.formatDistance(4800), "4,8 km");
    assert.equal(discovery.formatDistance(NaN), "");
});

test("category filtering keeps stable no-location order and nearest order when permitted", () => {
    const items = [
        { order: 0, category: "2", latitude: 46.02, longitude: 15 },
        { order: 1, category: "1", latitude: 46.001, longitude: 15 },
        { order: 2, category: "2", latitude: 46.002, longitude: 15 }
    ];
    assert.deepEqual(Array.from(discovery.visibleItems(items, "all", null), item => item.order), [0, 1, 2]);
    assert.deepEqual(Array.from(discovery.visibleItems(items, "2", null), item => item.order), [0, 2]);
    assert.deepEqual(Array.from(discovery.visibleItems(items, "all", origin), item => item.order), [1, 2, 0]);
    assert.deepEqual(Array.from(discovery.visibleItems(items, "2", origin), item => item.order), [2, 0]);
    assert.deepEqual(Array.from(items, item => item.order), [0, 1, 2]);
});

test("all seven category filters select only their places without a location", () => {
    const items = [7, 4, 1, 6, 2, 5, 3].map((category, order) => ({
        category: String(category), order, latitude: 46 + order / 100, longitude: 15
    }));
    assert.deepEqual(Array.from(discovery.visibleItems(items, "all", null), item => item.category),
        ["7", "4", "1", "6", "2", "5", "3"]);
    for (let category = 1; category <= 7; category++) {
        assert.deepEqual(Array.from(discovery.visibleItems(items, String(category), null), item => item.category),
            [String(category)]);
        assert.deepEqual(Array.from(discovery.visibleItems(items, String(category), origin), item => item.category),
            [String(category)]);
    }
    assert.equal(discovery.visibleItems(items, "8", null).length, 0);
});

test("discovery page keeps explicit location permission, handoffs, fallback and empty states", () => {
    const view = read("DoggyDrop/Views/Places/Index.cshtml");
    const css = read("DoggyDrop/wwwroot/css/place-discovery.css");
    const home = read("DoggyDrop/Views/Map/Index.cshtml");
    assert.match(view, /id="discoveryLocate"/);
    assert.match(source, /locate\.addEventListener\("click",/);
    assert.match(source, /navigator\.geolocation\.getCurrentPosition/);
    assert.doesNotMatch(source, /localStorage|sessionStorage|fetch\(|XMLHttpRequest|watchPosition|console\./);
    assert.match(view, /asp-controller="Map" asp-action="Index" asp-route-placeId="@place\.Id"/);
    assert.match(view, /asp-controller="Places" asp-action="Details" asp-route-id="@place\.Id"/);
    assert.match(view, /Trenutno še ni dodanih lokacij/);
    assert.match(view, /V tej kategoriji trenutno ni lokacij/);
    assert.match(view, /aria-pressed="true"/);
    assert.match(view, /place\.IconClass/);
    assert.match(view, /PlaceCategories\.All/);
    const presentation = read("DoggyDrop/Services/PlacePresentation.cs");
    for (const label of ["Veterinarji", "Trgovine", "Saloni", "Pasje šole", "Lokali", "Pasji parki", "Pasje plaže"])
        assert.ok(presentation.includes(label));
    assert.match(css, /object-fit: contain/);
    assert.match(css, /min-height: 44px/);
    assert.match(css, /overflow-x: auto/);
    assert.match(css, /min-width: 0/);
    assert.match(css, /safe-area-inset-bottom/);
    assert.match(home, /id="exploreToggle"/);
    assert.match(home, /id="exploreList"/);
    assert.match(home, /data-explore-filter="bin"/);
    assert.match(home, /asp-controller="Places" asp-action="Index"/);
    assert.match(home, /place\.categoryKey !== "other"/);
    assert.doesNotMatch(home, /place\.category === 1 \|\| place\.category === 2/);
});
