const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const root = path.resolve(__dirname, "..", "..");
const auth = require(path.join(root, "DoggyDrop/wwwroot/js/walk-recording-auth.js"));
const read = relative => fs.readFileSync(path.join(root, relative), "utf8");

function response(status, { url = "https://doggydrop.app/Walks/AddPoint/42", type = "application/json", html = "", location = null } = {}) {
    return {
        status, url,
        headers: { get(name) { return name === "content-type" ? type : name === "location" ? location : null; } },
        clone() { return { async text() { return html; } }; }
    };
}

test("accepted AddPoint JSON does not suspend recording", async () => {
    const guard = auth.createGuard(42, () => assert.fail("auth callback must not run"));
    assert.equal(await guard.check(response(200)), false);
    assert.equal(guard.canRecord(), true);
});

test("401 and 403 stop future recording attempts once", async () => {
    for (const status of [401, 403]) {
        let notices = 0;
        let attempts = 0;
        const guard = auth.createGuard(42, () => { notices++; });
        async function attempt(result) {
            if (!guard.canRecord()) return;
            attempts++;
            await guard.check(result);
        }
        await attempt(response(status));
        await attempt(response(200));
        assert.equal(attempts, 1);
        assert.equal(notices, 1);
        assert.equal(guard.canRecord(), false);
        assert.equal(guard.loginUrl, "/Identity/Account/Login?returnUrl=%2FWalks%2FActive%2F42");
    }
});

test("followed login redirect and direct login URL are recognized before JSON parsing", async () => {
    const login = "https://doggydrop.app/Identity/Account/Login?ReturnUrl=%2FWalks%2FAddPoint%2F42";
    assert.equal(await auth.isAuthResponse(response(200, { url: login, type: "text/html" })), true);
    assert.equal(await auth.isAuthResponse(response(200, { url: login })), true);
    assert.equal(await auth.isAuthResponse(response(302, { location: login })), true);
});

test("HTTP 200 HTML DoggyDrop login form is recognized without a login URL", async () => {
    const html = '<form action="/" method="post" class="auth-form"><input name="Input.Email"></form>';
    assert.equal(await auth.isAuthResponse(response(200, { type: "text/html; charset=utf-8", html })), true);
});

test("non-login HTML, malformed JSON and network failures do not masquerade as auth loss", async () => {
    assert.equal(await auth.isAuthResponse(response(500, { type: "text/html", html: "<h1>Server error</h1>" })), false);
    assert.equal(await auth.isAuthResponse(response(200, { type: "application/json" })), false);
    let notices = 0;
    const guard = auth.createGuard(42, () => { notices++; });
    const failedFetch = async () => { throw new TypeError("network failure"); };
    await assert.rejects(failedFetch(), TypeError);
    assert.equal(guard.canRecord(), true);
    assert.equal(notices, 0);
});

test("login URL is local and rejects invalid Walk IDs", () => {
    for (const id of [0, -1, 1.5, Number.MAX_SAFE_INTEGER + 1, "//evil.example"]) {
        assert.throws(() => auth.createGuard(id, () => {}));
    }
    const url = new URL(auth.createGuard(42, () => {}).loginUrl, "https://doggydrop.app");
    assert.equal(url.origin, "https://doggydrop.app");
    assert.equal(url.searchParams.get("returnUrl"), "/Walks/Active/42");
});

test("Active and Home both check auth before parsing JSON and suspend their watchers", () => {
    const active = read("DoggyDrop/Views/Walks/Active.cshtml");
    const home = read("DoggyDrop/Views/Map/Index.cshtml");
    for (const view of [active, home]) {
        assert.match(view, /~\/js\/walk-recording-auth\.js/);
        assert.match(view, /await (?:recordingAuth|homeRecordingAuth)\.check\(response\)[\s\S]*?const data = await response\.json\(\)/);
        assert.match(view, /Prijava je potekla\. Beleženje je ustavljeno\./);
        assert.match(view, /Ponovno se prijavi/);
    }
    assert.match(active, /function handleRecordingAuthLost\(\) \{\s*stopTracking\(\)/);
    assert.match(active, /if \(isFinishing \|\| isTracking \|\| !recordingAuth\.canRecord\(\)\) return/);
    assert.match(home, /function handleHomeRecordingAuthLost\(\) \{\s*homeWalkTracking\.active = false;\s*stopHomeWalkWatch\(\)/);
    assert.match(home, /if \(!homeWalkTracking\.active \|\| !homeRecordingAuth\?\.canRecord\(\)\) return/);
});

test("Active and Home inline scripts remain syntactically valid after Razor values are rendered", () => {
    const active = read("DoggyDrop/Views/Walks/Active.cshtml")
        .match(/<script>\s*(const walkId =[\s\S]*?)<\/script>/)[1]
        .replaceAll("@Model.Id", "42")
        .replaceAll('@Model.StartedAt.ToString("O")', "2026-09-25T10:00:00Z")
        .replaceAll("@pointsJson", "[]")
        .replaceAll("@plannedRoutePointsJson", "[]")
        .replaceAll("@plannedStopsJson", "[]")
        .replaceAll("@completedStopIdsJson", "[]")
        .replace(/@Model\.DistanceMeters\.ToString\([^\r\n]+\)/, "0");
    const home = read("DoggyDrop/Views/Map/Index.cshtml")
        .match(/<script>\s*(const cartoBasemapKey =[\s\S]*?)<\/script>/)[1]
        .replaceAll("@cartoBasemapKeyJson", "null")
        .replaceAll("@binsJson", "[]")
        .replaceAll("@managedPlacesJson", "[]")
        .replaceAll("@myDogsJson", "[]")
        .replaceAll("@homeWalkTrailJson", "[]")
        .replaceAll("@homeWalkPlanJson", "[]")
        .replaceAll("@parkLocationsJson", "[]")
        .replace(/const requestVerificationToken = @Html\.Raw\([^\r\n]+/, 'const requestVerificationToken = "";')
        .replace(/const activeWalkStartedAt = @Html\.Raw\([^\r\n]+/, "const activeWalkStartedAt = null;")
        .replace("@(activeWalk?.Id ?? 0)", "42");
    assert.doesNotMatch(active, /@Model\.|@pointsJson|@plannedRoutePointsJson/);
    assert.doesNotMatch(home, /@(?:Html|homeWalk|parkLocations|managedPlaces|binsJson)/);
    assert.doesNotThrow(() => new vm.Script(active));
    assert.doesNotThrow(() => new vm.Script(home));
});
