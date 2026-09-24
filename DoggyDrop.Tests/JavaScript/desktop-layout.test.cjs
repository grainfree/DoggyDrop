const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");

const root = path.resolve(__dirname, "..", "..");
const read = relative => fs.readFileSync(path.join(root, relative), "utf8");

test("one primary nav has five destinations before page content", () => {
    const layout = read("DoggyDrop/Views/Shared/_Layout.cshtml");
    const nav = read("DoggyDrop/Views/Shared/_PrimaryNavigation.cshtml");
    assert.equal((layout.match(/name="_PrimaryNavigation"/g) || []).length, 2);
    assert.equal((nav.match(/<nav class="@Model"/g) || []).length, 1);
    const brand = layout.indexOf('class="app-brand"');
    const desktopNav = layout.indexOf('model="desktopNavigationClass"');
    const utilities = layout.indexOf('class="app-topbar__actions"');
    const mobileNav = layout.indexOf('model="mobileNavigationClass"');
    assert.ok(brand < desktopNav && desktopNav < utilities && utilities < mobileNav);
    assert.ok(mobileNav < layout.indexOf('@RenderBody()'));
    for (const label of ["Domov", "Psi", "Sprehodi", "Skupnost", "Profil"]) {
        assert.match(nav, new RegExp(`<span>${label}</span>`));
    }
    assert.equal((nav.match(/class="app-bottom-nav__item /g) || []).length, 5);
});

test("desktop nav uses the header and mobile keeps the fixed bottom nav", () => {
    const css = read("DoggyDrop/wwwroot/css/site.css");
    assert.match(css, /\.app-bottom-nav \{[^}]*position: fixed;[^}]*bottom: 12px;/);
    assert.match(css, /\.app-desktop-nav \{\s*display: none;/);
    assert.match(css, /@media \(min-width: 1024px\) \{[\s\S]*?\.app-bottom-nav \{ display: none; \}/);
    assert.match(css, /@media \(min-width: 1024px\) \{[\s\S]*?\.app-desktop-nav \{[^}]*display: flex;/);
    assert.match(css, /@media \(min-width: 1024px\) \{[\s\S]*?\.app-content \{ padding-bottom: 0; \}/);
});

test("Home has one desktop left stack, compact action rail and unchanged Places", () => {
    const map = read("DoggyDrop/Views/Map/Index.cshtml");
    const panelStart = map.indexOf('<div class="map-home-panel">');
    const panelEnd = map.indexOf('</div>\n\n<aside id="filterPanel"', panelStart);
    assert.ok(panelStart >= 0 && panelEnd > panelStart);
    for (const item of ['class="map-quick-intro"', 'id="firstDogPrompt"', 'id="mapFounderPrompt"']) {
        const itemIndex = map.indexOf(item, panelStart);
        assert.ok(itemIndex >= panelStart && itemIndex < panelEnd);
    }
    assert.match(map, /\.map-home-panel,\s*\.map-nearby-content \{ display: contents; \}/);
    assert.match(map, /@@media \(min-width: 1024px\) \{[\s\S]*?\.map-home-panel \{[^}]*display: flex;/);
    assert.match(map, /@@media \(min-width: 1024px\) \{[\s\S]*?\.map-action-stack \{[^}]*width: 220px;/);
    assert.match(map, /managedPlaceLayer = buildManagedPlaceLayer\(managedPlaces\)\.addTo\(map\)/);
    assert.match(map, /<strong>@Model\.Count\(\)<\/strong>/);
});

test("Nearby has one desktop scroll body and PWA toast clears Home controls", () => {
    const map = read("DoggyDrop/Views/Map/Index.cshtml");
    const css = read("DoggyDrop/wwwroot/css/site.css");
    const layout = read("DoggyDrop/Views/Shared/_Layout.cshtml");
    assert.match(map, /id="nearbySuggestionsPanel"[\s\S]*?class="map-nearby-content"[\s\S]*?id="nearbySuggestionsList"/);
    assert.match(map, /\.map-explore-panel--nearby \.map-nearby-content \{[^}]*overflow-y: auto;/);
    assert.match(map, /\.map-explore-panel--nearby \.map-explore-list,[\s\S]*?\.map-explore-panel--nearby \.map-nearby-dogs__list \{[^}]*overflow: visible;/);
    assert.match(layout, /app-shell--map-home/);
    assert.match(css, /@media \(min-width: 768px\) \{\s*\.app-shell--map-home #pwaPrompt/);
    assert.match(css, /\.app-shell--map-home #pwaPrompt \{[^}]*top: 150px !important;[^}]*bottom: auto !important;/);
});
