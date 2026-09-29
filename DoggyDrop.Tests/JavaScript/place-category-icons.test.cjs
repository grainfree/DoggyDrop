const test = require('node:test'), assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const {loadPopup} = require('./helpers/place-popup-dom.cjs');
const read = p => fs.readFileSync(path.join(__dirname, '../..', p), 'utf8');
const definition = read('DoggyDrop/Services/PlacePresentation.cs');
const css = read('DoggyDrop/wwwroot/css/place-category-icons.css');
const categories = [...definition.matchAll(/new\(PlaceCategory\.(\w+), "([^"]+)", "[^"]+", "([^"]+)", "([^"]+)", (true|false)\)/g)]
    .map(m => ({enum: m[1], categoryLabel: m[2], categoryKey: m[3], iconClass: m[4], isCommercial: m[5] === 'true'}));
function drawing(p) {
    const selector = p.iconClass === 'dd-place-icon--other' ? 'dd-place-icon' : p.iconClass;
    const match = css.match(new RegExp('\\.'+selector+' \\{ --place-icon: url\\("data:image/svg\\+xml,([^"]+)"\\); \\}'));
    assert.ok(match, `${p.enum || selector} has a bundled drawing`);return decodeURIComponent(match[1]);
}
test('every enum category has one authoritative presentation and a distinct safe bundled SVG', () => {
    const model = read('DoggyDrop/Models/Place.cs').split('public sealed class')[0];
    const enums = [...model.matchAll(/(\w+)\s*=\s*\d+/g)].map(m => m[1]);
    assert.deepEqual(categories.map(c => c.enum), enums);
    assert.equal(new Set(categories.map(drawing)).size, enums.length);
    for (const c of [...categories, {iconClass:'dd-place-icon--other'}]) {
        const svg = drawing(c);
        assert.match(svg, /viewBox="0 0 24 24"/);
        assert.match(svg, /stroke-width="1.9"/);
        assert.doesNotMatch(svg, /<(?!\/?(?:svg|path|circle|ellipse)\b)|\bon\w+=|href=|<text|<image|script/i);
    }
});
test('every category uses the same marker and popup drawing, not letters or Bootstrap tree', () => {
    const {marker,popup} = loadPopup();
    for (const c of categories) {
        const p = {...c, id:1, name:'Pasje igrišče Vir'};
        const icon = marker.createIcon(p), card = popup.create(p);
        assert.match(icon.html, new RegExp(c.iconClass));
        assert.equal(card.querySelector('i').className, 'dd-place-icon '+c.iconClass);
        assert.doesNotMatch(icon.html, /bi-tree|>\s*[PCWV]\s*</);
        assert.equal(card.querySelector('i').attributes['aria-hidden'], 'true');
        assert.match(icon.html, /aria-label="Pasje igrišče Vir – /);
        assert.match(icon.html, /aria-hidden="true"/);
    }
});
test('DogPark dog and DogBeach profile-with-waves stay distinct with no old tree fallback', () => {
    const dog = categories.find(c => c.enum === 'DogPark'), beach = categories.find(c => c.enum === 'DogBeach');
    assert.notEqual(drawing(dog), drawing(beach));
    assert.equal(dog.iconClass, 'dd-place-icon--dog-park');
    assert.equal(beach.iconClass, 'dd-place-icon--dog-beach');
    assert.doesNotMatch(definition, /bi-tree|bi-water/);
});
test('hostile text/logo cannot supply SVG or an arbitrary CSS class; unknown uses a pin', () => {
    const {marker,popup} = loadPopup();
    const attack = '\"><svg onload=alert(1)><script>x</script>';
    const p = {...categories[1], id:1, name:attack,address:attack,logoUrl:'javascript:'+attack,iconClass:attack};
    const html=marker.createIcon(p).html, card=popup.create(p);
    assert.doesNotMatch(html, /<svg|<script|<img/);assert.match(html, /dd-place-icon--other/);
    assert.equal(card.querySelector('h3').textContent, attack);assert.equal(card.querySelector('svg'),null);
    assert.equal(card.querySelector('img'),null);assert.equal(marker.iconClass({iconClass:'bi-tree-fill'}),'dd-place-icon--other');
    // Syntactically valid future/unknown classes retain the base pin mask.
    assert.match(css, /\.dd-place-icon \{ --place-icon:/);
});
test('all commercial category logos win on load and fall back independently on failure', () => {
    const {marker,popup} = loadPopup();
    for (const c of categories) {
        const card=popup.create({...c,id:1,name:'Mr.Pet',logoUrl:'https://fixture.invalid/logo.webp'});
        const img=card.querySelector('img');
        if (!c.isCommercial) {assert.equal(img,null);continue;}
        marker.attachImage(card);img.events.load();
        assert.ok(img.parentElement.classList.contains('has-image'));
        img.events.error();assert.equal(img.hidden,true);
        assert.equal(img.parentElement.classList.contains('has-image'),false);
        assert.equal(card.querySelector('i').className, 'dd-place-icon '+c.iconClass);
    }
});
test('new drawings preserve marker geometry and selected/Featured treatment for every category', () => {
    const {marker}=loadPopup();
    for(const c of categories) {
        const m=marker.createIcon({...c,isCurrentlyFeatured:true},{selected:true});
        assert.deepEqual(Array.from(m.iconSize),[52,52]);assert.deepEqual(Array.from(m.iconAnchor),[26,26]);
        assert.deepEqual(Array.from(m.popupAnchor),[0,-28]);assert.match(m.html,/managed-place-pin--selected/);
        assert.equal(m.html.includes('managed-place-pin--featured'),c.isCommercial);
    }
});
test('legacy park/water/cafe markers use trusted central icons without changing layer dimensions', () => {
    const map=read('DoggyDrop/Views/Map/Index.cshtml');
    const fn=map.slice(map.indexOf('function createPlaceIcon(type)'),map.indexOf('document.addEventListener("DOMContentLoaded"'));
    assert.doesNotMatch(fn,/\? "P"|\? "W"|: "C"/);
    assert.match(fn,/PlaceCategories.Get/);assert.match(fn,/iconSize: \[40, 40\]/);
    assert.match(fn,/iconAnchor: \[20, 20\]/);assert.match(fn,/popupAnchor: \[0, -22\]/);
});
test('Details logo uses the shared handler for cached success, missing and broken images', () => {
    const vm=require('node:vm');
    for(const state of ['success','cached-failure','late-failure','missing']) {
        const {document,marker}=loadPopup();
        const container=document.createElement('span'),icon=document.createElement('i');
        container.className='places-details__logo';icon.className='dd-place-icon dd-place-icon--pet-shop';container.append(icon);
        if(state!=='missing') {
            const image=document.createElement('img');image.className='managed-place-image';
            image.complete=state!=='late-failure';image.naturalWidth=state==='success'?80:0;container.append(image);
        }
        const documentStub={querySelector:selector=>selector==='.places-details__logo'?container:null,getElementById:()=>null};
        vm.runInNewContext(read('DoggyDrop/wwwroot/js/place-details.js'),{document:documentStub,window:{DoggyDropPlaceMarker:marker}});
        const image=container.querySelector('img');
        if(state==='late-failure')image.events.error();
        assert.equal(container.classList.contains('has-image'),state==='success');
        if(state.endsWith('failure'))assert.equal(image.hidden,true);
        assert.equal(container.querySelector('i'),icon);
    }
    assert.match(read('DoggyDrop/wwwroot/css/places.css'),/\.places-details__logo\.has-image i \{ display: none;/);
});

test('dog-specific beach artwork is distinct from legacy generic water without reclassifying data', () => {
    const beach=drawing(categories.find(c=>c.enum==='DogBeach'));
    const water=drawing({iconClass:'dd-place-icon--water'});
    assert.notEqual(beach,water);
    assert.match(read('DoggyDrop/Views/Map/Index.cshtml'),/water: \["dd-place-icon--water", "Voda"\]/);
    assert.doesNotMatch(water,/<(?!\/?(?:svg|path)\b)|href=|script/i);
});

// Owner-approved geometry snapshots: intentional visual changes require an explicit review.
const approvedArtwork = {
    "Veterinarian": {
        "iconClass": "dd-place-icon--veterinarian",
        "paths": [
            "m5 5-3-2-1 5 3 2m9-5 3-2 1 5-3 2M5 5q4-3 8 0l1 5v6q-5 5-10 0v-6l1-5Zm2 8 2 2 2-2M9 15v2",
            "M17 17h6m-3-3v6"
        ],
        "circles": []
    },
    "DogSchool": {
        "iconClass": "dd-place-icon--dog-school",
        "paths": [
            "M3 21v-7l2-4V6l5 4h4v4l-4 2v5M5 10l3 4",
            "m14 5 3 3 5-6M3 21h11"
        ],
        "circles": []
    },
    "DogPark": {
        "iconClass": "dd-place-icon--dog-park",
        "paths": [
            "M7 5 3 3 1 8l4 4M17 5l4-2 2 5-4 4M7 5c2-2 8-2 10 0l2 7v4c0 6-14 6-14 0v-4l2-7Z",
            "m10 14 2 2 2-2h-4Zm2 2v3"
        ],
        "circles": [
            "cx=\"9\" cy=\"10\" r=\".7\"",
            "cx=\"15\" cy=\"10\" r=\".7\""
        ]
    },
    "DogBeach": {
        "iconClass": "dd-place-icon--dog-beach",
        "paths": [
            "M5 14V8l3-5 4 4h6l3 3-3 3h-5v2M8 3v7l4-3",
            "M2 18q2.5-3 5 0t5 0 5 0 5 0M2 22q2.5-3 5 0t5 0 5 0 5 0"
        ],
        "circles": []
    }
};
for (const [category, expected] of Object.entries(approvedArtwork)) {
    test(`owner-approved ${category} artwork retains its exact dog/care/training/water cues`, () => {
        const presentation=categories.find(c=>c.enum===category);
        assert.equal(presentation.iconClass,expected.iconClass);
        const svg=drawing(presentation);
        assert.deepEqual([...svg.matchAll(/<path d="([^"]+)"/g)].map(m=>m[1]),expected.paths);
        assert.deepEqual([...svg.matchAll(/<circle ([^>]+?)\/>/g)].map(m=>m[1]),expected.circles);
    });
}
