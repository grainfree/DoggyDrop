const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");

const view = fs.readFileSync(path.resolve(__dirname, "../../DoggyDrop/Views/Walks/Active.cshtml"), "utf8");
const cockpit = view.slice(view.indexOf('<aside class="active-walk-cockpit"'), view.indexOf('<div class="active-walk-metrics"'));
const mobileCss = view.slice(view.indexOf('@@media (max-width: 575.98px)'));

test("collapsed mobile cockpit retains one visible Finish control", () => {
    assert.match(cockpit, /id="walkCockpitToggle"[^>]*aria-expanded="false"/);
    assert.match(cockpit, /V živo · @Model\.Dog\?\.Name/);
    assert.match(cockpit, /id="cockpitRecording"/);
    assert.match(cockpit, /id="cockpitDuration"/);
    assert.match(cockpit, /id="cockpitDistance"/);
    assert.match(cockpit, /id="cockpitGpsState"/);
    assert.ok(cockpit.indexOf('id="finishWalkMobileButton"') < cockpit.indexOf('id="walkCockpitActions"'));
    assert.equal((view.match(/id="finishWalkMobileButton"/g) || []).length, 1);
    assert.match(mobileCss, /\.active-walk-cockpit__finish\s*\{[^}]*min-height:\s*52px/);
});

test("secondary controls retain the same GPS toggle and keep debug opt-in", () => {
    assert.match(cockpit, /id="walkCockpitActions"[^>]*hidden/);
    assert.match(cockpit, /id="mobileTrackingSlot"/);
    assert.match(cockpit, /Fotografija[\s\S]*?Najdi koš[\s\S]*?Dodaj koš/);
    assert.match(view, /\[trackingToggle, document\.getElementById\("mobileTrackingSlot"\)\]/);
    assert.match(view, /trackingToggle\.addEventListener\("click", toggleTracking\)/);
    assert.match(view, /@if \(debugGpsEnabled\)[\s\S]*?id="gpsDebugPanel"/);
    assert.match(view, /\[gpsDebugPanel, document\.getElementById\("mobileGpsDebugSlot"\)\]/);
});

test("normal mobile map has no top GPS card or duplicate metric overlay", () => {
    assert.match(mobileCss, /\.active-walk-map-card__header\s*\{\s*display:\s*none;/);
    assert.match(mobileCss, /\.active-walk-metrics\s*\{\s*display:\s*none;/);
    assert.doesNotMatch(cockpit, /cockpitPointCount|GPS točke/);
    assert.match(view, /id="pointCount"/);
    assert.match(view, /id="gpsDebugPersisted"/);
    assert.match(view, /accuracy <= 50 \? "GPS dober" : "GPS šibek"/);
    assert.match(view, /cockpitGpsAccuracy\.textContent = `· \$\{accuracy\} m`/);
});

test("styled modal confirms through the existing Finish flow", () => {
    assert.doesNotMatch(view, /window\.confirm\(/);
    assert.match(view, /<dialog id="finishWalkDialog"[^>]*aria-labelledby="finishWalkDialogTitle"[^>]*aria-describedby="finishWalkDialogDescription"/);
    assert.match(view, /id="confirmFinishWalk"[^>]*>Zaključi sprehod/);
    assert.match(view, /id="cancelFinishWalk"[^>]*>Nadaljuj sprehod/);
    assert.match(view, /finishWalkForm\?\.addEventListener\("submit", requestFinishConfirmation\)/);
    assert.match(view, /finishWalkDialog\.showModal\(\)/);
    assert.match(view, /finishWalkDialog\.close\(\);\s*void finishWalk\(\)/);
    assert.match(view, /if \(!isFinishing\) finishWalkTrigger\?\.focus\(\)/);
    assert.match(view, /async function finishWalk\(\)\s*\{\s*if \(isFinishing\) return;\s*const wasTracking = isTracking;\s*isFinishing = true;\s*stopTracking\(\)/);
    assert.match(view, /finishWalkButton\.disabled = true;[\s\S]*?finishWalkMobileButton\.disabled = true;/);
});

test("mobile sheet and attribution retain safe-area clearance", () => {
    assert.match(mobileCss, /\.active-walk-cockpit\s*\{[^}]*bottom: env\(safe-area-inset-bottom, 0px\)/);
    assert.match(mobileCss, /\.active-walk-page \.leaflet-control-attribution\s*\{[^}]*--walk-cockpit-height/);
    assert.match(mobileCss, /body:has\(\.active-walk-page\) \.app-bottom-nav\s*\{\s*display: none !important;/);
    assert.match(view, /new ResizeObserver\(entries =>/);
    assert.match(view, /map\?\.invalidateSize\(\{ pan: false \}\)/);
});
