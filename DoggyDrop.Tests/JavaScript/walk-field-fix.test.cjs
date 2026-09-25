const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");

const root = path.resolve(__dirname, "..", "..");
const read = relative => fs.readFileSync(path.join(root, relative), "utf8");
const proximity = require(path.join(root, "DoggyDrop/wwwroot/js/active-bin-proximity.js"));

test("nearby bin alerts require accepted-quality GPS and a bin within 120 m", () => {
    const tracker = proximity.createTracker(1);
    assert.equal(tracker.check([{ id: 1, distanceMeters: 121 }], 15, 1000), null);
    assert.equal(tracker.check([{ id: 1, distanceMeters: 60 }], 80, 1000), null);
    assert.equal(tracker.check([{ id: 1, distanceMeters: 60 }], 15, 1000).id, 1);
});

test("nearest bin wins, same bin is once per walk, different bin waits for cooldown", () => {
    const tracker = proximity.createTracker(1);
    assert.equal(tracker.check([{ id: 1, distanceMeters: 90 }, { id: 2, distanceMeters: 40 }], 15, 1000).id, 2);
    assert.equal(tracker.check([{ id: 2, distanceMeters: 30 }], 15, 20000), null);
    assert.equal(tracker.check([{ id: 1, distanceMeters: 30 }], 15, 2000), null);
    assert.equal(tracker.check([{ id: 1, distanceMeters: 30 }], 15, 20000).id, 1);
    assert.deepEqual(tracker.alertedIds(), [2, 1]);
    assert.equal(proximity.createTracker(1, tracker.state(), 20000).check([{ id: 2, distanceMeters: 20 }], 10, 20000), null);
});

test("walk-scoped state keeps the remaining cooldown across navigation handoff", () => {
    const firstPage = proximity.createTracker(8);
    assert.equal(firstPage.check([{ id: 1, distanceMeters: 30 }], 12, 100000).id, 1);
    const saved = firstPage.state();
    assert.deepEqual(saved, { walkId: 8, alertedBinIds: [1], alertedPlannedStopIds: [], lastAlertAt: 100000 });
    const returnedPage = proximity.createTracker(8, saved, 105000);
    assert.equal(returnedPage.check([{ id: 1, distanceMeters: 20 }], 12, 105000), null);
    assert.equal(returnedPage.check([{ id: 2, distanceMeters: 20 }], 12, 105000), null);
    assert.equal(returnedPage.check([{ id: 2, distanceMeters: 20 }], 12, 115001).id, 2);
    assert.equal(proximity.createTracker(9, saved, 105000).check([{ id: 1, distanceMeters: 20 }], 12, 105000).id, 1);
});

test("planned stop alert survives navigation handoff and uses its own 85 m threshold", () => {
    const firstPage = proximity.createTracker(8);
    assert.equal(firstPage.check([], 10, 100000, [{ id: 12, distanceMeters: 86 }]), null);
    const planned = firstPage.check([], 10, 100000, [{ id: 12, distanceMeters: 80 }]);
    assert.equal(planned.source, "planned");
    const returnedPage = proximity.createTracker(8, firstPage.state(), 120000);
    assert.equal(returnedPage.check([], 10, 120000, [{ id: 12, distanceMeters: 20 }]), null);
    assert.deepEqual(returnedPage.state().alertedPlannedStopIds, [12]);
    assert.equal(proximity.createTracker(9, firstPage.state(), 120000)
        .check([], 10, 120000, [{ id: 12, distanceMeters: 20 }]).source, "planned");
});

test("planned and public alerts share one cooldown in both directions", () => {
    const plannedFirst = proximity.createTracker(8);
    assert.equal(plannedFirst.check([], 10, 100000, [{ id: 1, distanceMeters: 20 }]).source, "planned");
    const afterHandoff = proximity.createTracker(8, plannedFirst.state(), 105000);
    assert.equal(afterHandoff.check([{ id: 2, distanceMeters: 20 }], 10, 105000), null);
    assert.equal(afterHandoff.check([{ id: 2, distanceMeters: 20 }], 10, 115000).source, "public");

    const publicFirst = proximity.createTracker(8);
    assert.equal(publicFirst.check([{ id: 2, distanceMeters: 20 }], 10, 100000).source, "public");
    assert.equal(publicFirst.check([], 10, 105000, [{ id: 1, distanceMeters: 20 }]), null);
    assert.equal(publicFirst.check([], 10, 115000, [{ id: 1, distanceMeters: 20 }]).source, "planned");
});

test("planned stops take priority over public bins without ID collision", () => {
    const tracker = proximity.createTracker(8);
    const planned = tracker.check([{ id: 12, distanceMeters: 5 }], 10, 100000,
        [{ id: 12, distanceMeters: 80 }, { id: 13, distanceMeters: 20 }]);
    assert.equal(planned.source, "planned");
    assert.equal(planned.id, 13);
    assert.deepEqual(tracker.state().alertedBinIds, []);
    assert.deepEqual(tracker.state().alertedPlannedStopIds, [13]);
    assert.equal(tracker.check([{ id: 12, distanceMeters: 5 }], 10, 115000).source, "public");
    assert.deepEqual(tracker.state().alertedBinIds, [12]);
    assert.equal(tracker.check([], 10, 130000, [{ id: 12, distanceMeters: 20 }]).source, "planned");
    assert.deepEqual(tracker.state().alertedPlannedStopIds, [13, 12]);
});

test("malformed planned state is ignored safely", () => {
    const tracker = proximity.createTracker(8,
        { walkId: 8, alertedBinIds: "bad", alertedPlannedStopIds: "bad", lastAlertAt: "bad" }, 100000);
    assert.equal(tracker.check([], 10, 100000, [{ id: 1, distanceMeters: 20 }]).source, "planned");
    const active = read("DoggyDrop/Views/Walks/Active.cshtml");
    assert.match(active, /JSON\.parse\(sessionStorage\.getItem\(binAlertStorageKey\)/);
    assert.match(active, /const nearby = publicBinAlerts\.check\(candidates, accuracyMeters, Date\.now\(\), plannedCandidates\)/);
    assert.doesNotMatch(active, /alertedPlannedBinStops/);
});

test("expired, future and malformed cooldown times are handled within a bounded window", () => {
    const state = { walkId: 8, alertedBinIds: [1], lastAlertAt: 100000 };
    assert.equal(proximity.createTracker(8, state, 120000).check([{ id: 2, distanceMeters: 20 }], 12, 120000).id, 2);
    const malformed = proximity.createTracker(8, { ...state, lastAlertAt: "bad" }, 105000);
    assert.equal(malformed.check([{ id: 2, distanceMeters: 20 }], 12, 105000), null);
    assert.equal(malformed.check([{ id: 2, distanceMeters: 20 }], 12, 120001).id, 2);
    const future = proximity.createTracker(8, { ...state, lastAlertAt: 999999 }, 105000);
    assert.equal(future.check([{ id: 2, distanceMeters: 20 }], 12, 105000), null);
    assert.equal(future.check([{ id: 2, distanceMeters: 20 }], 12, 120001).id, 2);
});

test("active walk loads approved bins once into a separate marker layer and uses accepted points", () => {
    const active = read("DoggyDrop/Views/Walks/Active.cshtml");
    const home = read("DoggyDrop/Views/Map/Index.cshtml");
    assert.match(active, /approvedBinLayer = L\.layerGroup\(\)\.addTo\(map\)/);
    assert.match(active, /fetch\("\/Map\/FindNearest"/);
    assert.match(active, /void loadApprovedBins\(\)/);
    assert.equal((active.match(/void loadApprovedBins\(\)/g) || []).length, 1);
    assert.match(active, /if \(data\.outcome !== "accepted"\)[\s\S]*?updateRoute\(lat, lng, position\.coords\.accuracy\)/);
    assert.match(active, /routeLine = L\.polyline\(routePoints/);
    assert.match(active, /DoggyDropBinMarker\.createIcon\(bin\)/);
    assert.match(active, /sessionStorage\.setItem\(binAlertStorageKey/);
    assert.match(active, /binProximityAction\.href = `\/Map\?navigateBin=\$\{binId\}`/);
    assert.match(home, /navigateToBinInApp\(requestedBinId\)/);
    assert.match(active, /document\.hidden \|\| !Number\.isFinite\(accuracyMeters\)/);
});

test("Home and Active Walk share the same bin marker asset", () => {
    const home = read("DoggyDrop/Views/Map/Index.cshtml");
    const active = read("DoggyDrop/Views/Walks/Active.cshtml");
    const marker = read("DoggyDrop/wwwroot/js/bin-marker.js");
    for (const view of [home, active]) {
        assert.match(view, /~\/css\/bin-marker\.css/);
        assert.match(view, /~\/js\/bin-marker\.js/);
    }
    assert.match(marker, /map-bin-marker__icon/);
    assert.match(marker, /map-bin-marker--missing/);
});

test("Home community presence has one visibility-aware 60-second refresh path", () => {
    const home = read("DoggyDrop/Views/Map/Index.cshtml");
    assert.equal((home.match(/communityRefreshTimer = window\.setInterval/g) || []).length, 1);
    assert.match(home, /communityRefreshTimer = window\.setInterval\(\(\) => void loadCommunityHeatmap\(\), 60000\)/);
    assert.match(home, /if \(communityRefreshTimer !== null \|\| document\.hidden\) return/);
    assert.match(home, /if \(document\.hidden\) stopCommunityRefresh\(\);[\s\S]*?void loadCommunityHeatmap\(\);[\s\S]*?startCommunityRefresh\(\)/);
    assert.match(home, /communityHeatRange = button\.dataset\.heatmapRange[\s\S]*?loadCommunityHeatmap\(\)/);
    assert.match(home, /requestId !== communityRequestId \|\| document\.hidden/);
    assert.match(home, /Number\(data\.activeWalkers \|\| 0\) === 1[\s\S]*?"aktiven" : "aktivnih"/);
});

test("History uses explicit Slovenian formatting and compact distance layout", () => {
    const history = read("DoggyDrop/Views/Walks/Index.cshtml");
    assert.match(history, /SlovenianFormatting\.WalkDistance\(walk\.DistanceMeters\)/);
    assert.match(history, /SlovenianFormatting\.PhotoCount\(photoCount\)/);
    assert.doesNotMatch(history, /walk photo|@\(walk\.DistanceMeters \/ 1000\)\.ToString/);
    assert.match(history, /\.walk-history__details \{ min-width: 0; overflow-wrap: anywhere; \}/);
    assert.match(history, /\.walk-history__distance \{ flex: 0 0 auto; white-space: nowrap; \}/);
});

test("Memory hero is portrait and bounded without changing gallery or share output", () => {
    const details = read("DoggyDrop/Views/Walks/Details.cshtml");
    assert.match(details, /\.walk-memory__cover \{[^}]*width: min\(100%, 360px\);[^}]*aspect-ratio: 4 \/ 5;/);
    assert.match(details, /\.walk-memory__cover img \{[^}]*object-fit: cover;[^}]*object-position: center 40%;/);
    assert.match(details, /\.walk-photo-grid \{/);
});
