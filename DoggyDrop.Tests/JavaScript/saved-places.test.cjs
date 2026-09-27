const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");
const root = path.resolve(__dirname, "../..");
const read = file => fs.readFileSync(path.join(root, "DoggyDrop", file), "utf8");
const control = read("Views/Shared/_PlaceSave.cshtml");
const card = read("Views/Shared/_PlaceCard.cshtml");
const saved = read("Views/SavedPlaces/Index.cshtml");
const discovery = read("Views/Places/Index.cshtml");
const details = read("Views/Places/Details.cshtml");
const css = read("wwwroot/css/place-save.css");
const script = read("wwwroot/js/place-card.js");

test("saved and Discovery pages share cards and the labelled save control", () => {
    for (const page of [saved, discovery]) assert.match(page, /<partial name="_PlaceCard"/);
    for (const page of [card, details]) assert.match(page, /<partial name="_PlaceSave"/);
    for (const field of ["Name", "CategoryLabel", "Address", "IconClass", "LogoUrl"])
        assert.ok(card.includes(`place.${field}`));
    assert.match(card, /place-discovery-card__distance/);
    assert.match(card, /Na zemljevidu/);
    assert.match(card, /Podrobnosti/);
    assert.match(discovery, /Model.SavedPlaceIds.Contains\(place.Id\)/);
    assert.match(details, /Model.IsSaved/);
});

test("save forms have a no-JS POST fallback and saved state is not color-only", () => {
    assert.match(control, /User.Identity\?\.IsAuthenticated == true/);
    assert.match(control, /asp-controller="SavedPlaces" asp-action="@\(Model.IsSaved \? "Remove" : "Save"\)" method="post"/);
    assert.match(control, /name="placeId"/);
    assert.match(control, /name="returnUrl"/);
    assert.doesNotMatch(control, /name="UserId"|asp-antiforgery="false"/i);
    assert.match(control, /aria-label="@\(Model.IsSaved/);
    for (const label of ["Shranjeno", "Shrani", "Odstrani", "bi-bookmark-fill", "bi-bookmark"])
        assert.ok(control.includes(label));
    assert.doesNotMatch(script, /fetch\(|alert\(|geolocation|localStorage|XMLHttpRequest/);
});

test("anonymous saving explains login via a native disclosure with a local page return", () => {
    assert.match(control, /<details class="place-save place-save--anonymous">/);
    assert.match(control, /<summary[^>]*aria-label="Shrani: @Model.Name"/);
    assert.match(control, /Za shranjevanje lokacij se prijavi/);
    assert.match(control, /asp-page="\/Account\/Login" asp-route-returnUrl="@Model.ReturnUrl">Prijava/);
    assert.match(discovery, /Url.Action\("Index", "Places"\)/);
    assert.match(details, /Url.Action\("Details", "Places", new \{ id = Model.Id \}\)/);
});

test("private Saved page has the specified empty state and no geolocation dependency", () => {
    for (const text of ["Shranjene lokacije", "Še nimaš shranjenih lokacij.",
        "Shrani veterinarja, trgovino, pasji park ali drugo lokacijo, da jo hitro najdeš.", "Razišči lokacije"])
        assert.ok(saved.includes(text));
    assert.match(saved, /asp-controller="Places" asp-action="Index"/);
    assert.doesNotMatch(saved, /discoveryLocate|place-discovery.js|geolocation/);
    for (const page of [saved, discovery, details]) assert.match(page, /role="status">@message/);
});

test("Profile and Discovery expose Saved without adding a bottom-navigation item", () => {
    const profile = read("Views/Home/UserProfile.cshtml");
    const nav = read("Views/Shared/_PrimaryNavigation.cshtml");
    assert.match(profile, /asp-controller="SavedPlaces" asp-action="Index"/);
    assert.match(discovery, /User.Identity\?\.IsAuthenticated == true/);
    assert.match(discovery, /asp-controller="SavedPlaces" asp-action="Index"/);
    assert.doesNotMatch(nav, /SavedPlaces/);
});

test("save controls wrap, have mobile touch targets and visible keyboard focus", () => {
    assert.match(css, /min-height: 44px/);
    assert.match(css, /:focus-visible[^}]*outline: 2px/);
    assert.match(css, /flex-wrap: wrap/);
    assert.match(css, /max-width: 100%/);
    assert.match(css, /place-discovery-card__save \{ grid-column: 1 \/ -1/);
    assert.match(read("wwwroot/css/place-discovery.css"), /safe-area-inset-bottom/);
    assert.ok(details.indexOf('Navodila za pot') < details.indexOf('<partial name="_PlaceSave"'));
});

test("Saved logo fallback handles cached failures and later errors without network requests", () => {
    const images = [
        { complete: true, naturalWidth: 0 },
        { complete: true, naturalWidth: 128 },
        { complete: false, naturalWidth: 0 }
    ].map(image => ({ ...image, hidden: false, handlers: {}, addEventListener(name, callback) { this.handlers[name] = callback; } }));
    vm.runInNewContext(script, { document: { querySelectorAll: () => images } });
    assert.equal(images[0].hidden, true);
    assert.equal(images[1].hidden, false);
    assert.equal(images[2].hidden, false);
    images[2].handlers.error();
    assert.equal(images[2].hidden, true);
    assert.match(card, /loading="lazy" decoding="async" referrerpolicy="no-referrer"/);
    assert.match(read("wwwroot/css/place-discovery.css"), /__logo img\[hidden\] \{ display: none/);
});
