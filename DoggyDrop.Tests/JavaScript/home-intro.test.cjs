const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");
const root = path.resolve(__dirname, "../..");
const read = name => fs.readFileSync(path.join(root, name), "utf8");
const script = read("DoggyDrop/wwwroot/js/home-intro.js");
const home = read("DoggyDrop/Views/Map/Index.cshtml");
const css = read("DoggyDrop/wwwroot/css/home-intro.css");
const layout = read("DoggyDrop/Views/Shared/_Layout.cshtml");
const key = "doggydrop.homeIntroDismissed.v1";

function run({ values = new Map(), active = false, absent = false, failRead = false, failWrite = false, failAccess = false } = {}) {
    const classes = new Set(active ? ["map-walk-active"] : []);
    const elements = Object.fromEntries(["homeIntro", "dismissHomeIntro", "homeIntroLogin", "map", "filterToggle", "exploreToggle", "communityHeatToggle", "homeContext", "communityHeatPanel"].map(id => [id, {
        hidden: true, handlers: {}, focused: false,
        attributes: {}, classList: { add(name) { this.added = name; } },
        setAttribute(name, value) { this.attributes[name] = value; },
        addEventListener(event, callback) { (this.handlers[event] ??= []).push(callback); },
        emit(event) { this.handlers[event]?.forEach(callback => callback()); },
        click() { this.emit("click"); }, focus() { this.focused = true; }
    }]));
    const controls = [elements.filterToggle, elements.exploreToggle, elements.communityHeatToggle];
    const document = {
        body: { classList: {
            contains: name => classes.has(name), remove: name => classes.delete(name),
            toggle(name, on) { if (on) classes.add(name); else classes.delete(name); }
        } },
        getElementById: id => absent && id === "homeIntro" ? null : elements[id],
        querySelectorAll: () => controls
    };
    const context = { document };
    Object.defineProperty(context, "localStorage", { get() {
        if (failAccess) throw new Error("SecurityError");
        return {
            getItem(k) { if (failRead) throw new Error("SecurityError"); return values.get(k) ?? null; },
            setItem(k, value) { if (failWrite) throw new Error("QuotaExceededError"); values.set(k, value); }
        };
    } });
    vm.runInNewContext(script, context);
    return { elements, classes, values };
}

test("first visit shows one non-modal intro without taking focus", () => {
    const page = run();
    assert.equal(page.elements.homeIntro.hidden, false);
    assert.ok(page.classes.has("home-intro-visible"));
    assert.equal(page.elements.map.focused, false);
    assert.doesNotMatch(home.match(/<section id="homeIntro"[\s\S]*?<\/section>/)[0], /aria-modal|role="dialog"/);
});

test("dismissal persists per-device preference and moves focus to the map", () => {
    const page = run();
    page.elements.dismissHomeIntro.click();
    assert.equal(page.elements.homeIntro.hidden, true);
    assert.equal(page.values.get(key), "true");
    assert.equal(page.elements.map.focused, true);
    assert.equal(page.classes.has("home-intro-visible"), false);
    assert.match(home, /id="map" tabindex="-1"/);
});

test("returning visit keeps intro hidden, including after login", () => {
    const first = run();
    first.elements.homeIntroLogin.click();
    assert.equal(first.elements.map.focused, false);
    const returning = run({ values: first.values });
    assert.equal(returning.elements.homeIntro.hidden, true);
    assert.equal(returning.classes.has("home-intro-visible"), false);
});

test("blocked storage access, reads and quota failures still allow exploration", () => {
    for (const options of [{ failAccess: true }, { failRead: true }, { failWrite: true }]) {
        const page = run(options);
        assert.equal(page.elements.homeIntro.hidden, false);
        assert.doesNotThrow(() => page.elements.dismissHomeIntro.click());
        assert.equal(page.elements.homeIntro.hidden, true);
        assert.equal(page.elements.map.focused, true);
    }
});

test("active walks and absent cards bypass onboarding without accessing storage", () => {
    for (const options of [{ active: true }, { absent: true }]) {
        const page = run({ ...options, failAccess: true });
        assert.equal(page.elements.homeIntro.hidden, true);
        assert.equal(page.classes.has("home-intro-visible"), false);
    }
    assert.match(home, /@if \(!hasActiveWalk\)\s*\{\s*<section id="homeIntro"/);
    assert.match(home, /@if \(!hasActiveWalk && \(showFirstWalkPrompt/);
});

test("map controls also complete first-run without moving focus", () => {
    for (const id of ["filterToggle", "exploreToggle", "communityHeatToggle"]) {
        const page = run();
        page.elements[id].click();
        assert.equal(page.elements.homeIntro.hidden, true);
        assert.equal(page.elements.map.focused, false);
        assert.equal(page.values.get(key), "true");
    }
});

test("anonymous login uses a fixed local Home return URL and exploration stays public", () => {
    assert.match(home, /@if \(!isSignedIn\)\s*\{\s*<a id="homeIntroLogin" asp-area="Identity" asp-page="\/Account\/Login" asp-route-returnUrl="\/Map"/);
    assert.match(home, /id="dismissHomeIntro" type="button"/);
    assert.match(home, /aria-label="Začni sprehod – potrebna je prijava"/);
    assert.doesNotMatch(script, /fetch\(|XMLHttpRequest|location\.(?:assign|replace)|location\.href\s*=/);
});

test("anonymous Add bin is available while Start Walk retains its login requirement", () => {
    const rail = home.slice(home.indexOf('<div class="map-action-stack @'), home.indexOf('<div class="map-status-pill">'));
    const addBin = rail.match(/<a\b[^>]*asp-controller="Map"[^>]*asp-action="Add"[^>]*>[\s\S]*?<\/a>/)?.[0];
    assert.ok(addBin, "Add bin must retain the Map/Add link");
    assert.match(addBin, /class="map-action-button"/);
    assert.match(addBin, /class="bi bi-plus-lg"/);
    assert.match(addBin, /<span>Predlagaj nov koš<\/span>/);
    assert.doesNotMatch(addBin, /bi-lock|prijav|disabled|isSignedIn|\shidden(?:\s|=|>)/i);
    assert.doesNotMatch(addBin.match(/^<a\b[^>]*>/)[0], /aria-hidden/i);
    const anonymousStart = rail.match(/else if \(!isSignedIn\)\s*\{([\s\S]*?)\}/)?.[1];
    assert.ok(anonymousStart);
    assert.match(anonymousStart, /asp-page="\/Account\/Login" asp-route-returnUrl="\/Walks#walkEntry"/);
    assert.match(anonymousStart, /aria-label="Začni sprehod – potrebna je prijava"/);
    assert.match(anonymousStart, /bi-lock/);
});

test("expanded community and contextual guidance do not compete for mobile space", () => {
    const { elements } = run({ values: new Map([[key, "true"]]) });
    elements.homeContext.open = true;
    elements.communityHeatToggle.click();
    assert.equal(elements.homeContext.open, false);
    elements.homeContext.open = true;
    elements.homeContext.emit("toggle");
    assert.equal(elements.communityHeatPanel.classList.added, "is-collapsed");
    assert.equal(elements.communityHeatToggle.attributes["aria-expanded"], "false");
    assert.equal(elements.communityHeatToggle.attributes["aria-label"], "Prikaži utrip skupnosti");
});

test("returning Home removes permanent marketing while retaining compact useful context", () => {
    assert.doesNotMatch(home, /<section class="map-quick-intro"|Naj bo vsak sprehod malo lažji/);
    assert.match(home, /<details id="homeContext" class="home-context">/);
    assert.match(home, /asp-controller="WeeklyGoals" asp-action="Index"/);
    assert.match(css, /\.home-intro\[hidden\] \{ display: none; \}/);
    assert.match(css, /\.home-context summary \{[^}]*min-height: 44px/);
});

test("Home retains four map actions, filters, bins and Places Discovery handoff", () => {
    const rail = home.slice(home.indexOf('<div class="map-action-stack @'), home.indexOf('<div class="map-status-pill">'));
    for (const text of ["Začni sprehod", "Predlagaj nov koš", "Najbližji koš", "Seznam"]) assert.ok(rail.includes(text));
    assert.match(rail, /findNearestTrashBin\(\)/);
    assert.match(home, /id="filterToggle"/);
    assert.match(home, /id="exploreList"/);
    assert.match(home, /asp-controller="Places" asp-action="Index"/);
    assert.match(home, /place\.categoryKey !== "other"/);
    assert.match(home, /placeId/);
    assert.match(home, /id="homeActiveWalkSheet"/);
});

test("mobile actions and filters reserve safe-area clearance and community stays compact", () => {
    assert.match(css, /\.map-action-stack \{ bottom: calc\(86px \+ env\(safe-area-inset-bottom, 0px\)\)/);
    assert.match(css, /\.map-filter-toggle \{ bottom: calc\(154px \+ env\(safe-area-inset-bottom, 0px\)\)/);
    assert.match(css, /\.map-community-heat__toggle \{ width: 44px; min-height: 44px/);
    assert.match(home, /id="communityHeatToggle"[^>]*aria-expanded="false"[^>]*aria-controls="communityHeatBody"/);
    assert.match(css, /@media \(prefers-reduced-motion: reduce\)/);
    assert.match(css, /:focus-visible/);
});

test("install guidance is contextual and existing Home settings tolerate disabled storage", () => {
    assert.doesNotMatch(layout, /new bootstrap.Toast|id="pwaPrompt"/);
    assert.match(layout, /<details id="pwaInstall"/);
    const settings = home.slice(home.indexOf('        function getMapSetting('), home.indexOf('        function buildPopupContent('));
    const context = { mapSettingsPrefix: "test.", localStorage: {
        getItem() { throw Error("blocked"); }, setItem() { throw Error("blocked"); }
    } };
    vm.createContext(context); vm.runInContext(settings, context);
    assert.equal(context.getMapSetting("bins", true), true);
    assert.doesNotThrow(() => context.saveMapSetting("bins", false));
});

test("intro preference has no server persistence, network or asset dependency", () => {
    assert.match(script, /doggydrop\.homeIntroDismissed\.v1/);
    assert.doesNotMatch(script, /fetch\(|XMLHttpRequest|sendBeacon|https?:|\/api\//);
    const intro = home.match(/<section id="homeIntro"[\s\S]*?<\/section>/)[0];
    assert.doesNotMatch(intro, /<img|<form|asp-controller/);
    assert.doesNotMatch(read("DoggyDrop/Models/ApplicationUser.cs"), /homeIntro|IntroDismissed/);
});
