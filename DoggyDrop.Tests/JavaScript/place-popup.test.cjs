const test = require('node:test'), assert = require('node:assert/strict');
const { loadPopup } = require('./helpers/place-popup-dom.cjs');
const place = { id: 3, name: 'Mr.Pet', categoryLabel: 'Trgovina', iconClass: 'bi-bag-fill', isCommercial: true,
    address: 'Naslov 1', logoUrl: 'https://example.invalid/logo.webp', detailsUrl: '/lokacije/3/mr-pet' };
test('compact Place card has server category, logo, canonical Details and existing navigation callback', () => {
    const { popup } = loadPopup(); let destination;
    const card = popup.create(place, { navigate: id => destination = id });
    assert.equal(card.querySelector('h3').textContent, place.name);
    assert.equal(card.querySelector('.managed-place-popup__category').textContent, place.categoryLabel);
    assert.equal(card.querySelector('img').src, place.logoUrl);
    assert.equal(card.querySelector('a').href, place.detailsUrl);
    card.querySelector('button').events.click(); assert.equal(destination, 3);
});
test('only the server-supplied Admin route enables the secondary action for the exact ID', () => {
    const { popup } = loadPopup();
    for (const adminEditBase of [null, undefined, '', 'https://evil.invalid', '//evil.invalid', '/Other/Edit'])
        assert.equal(popup.create(place, { adminEditBase }).querySelector('.managed-place-popup__edit'), null);
    assert.equal(popup.create(place, { adminEditBase: '/AdminPlaces/Edit' }).querySelector('.managed-place-popup__edit').href,
        '/AdminPlaces/Edit/3?returnTo=map');
});
test('missing address/logo keeps a compact category header with no blank address row', () => {
    const card = loadPopup().popup.create({ ...place, logoUrl: null, address: ' ' });
    assert.equal(card.querySelector('img'), null); assert.equal(card.querySelector('p'), null);
    assert.equal(card.querySelector('i').className, 'bi bi-bag-fill');
    assert.ok(card.querySelector('.managed-place-popup__media'));
});
test('dog destinations suppress logos and Featured even if a URL was supplied', () => {
    const { popup } = loadPopup();
    for (const iconClass of ['bi-tree-fill', 'bi-water']) {
        const card = popup.create({ ...place, isCommercial: false, iconClass, isCurrentlyFeatured: true });
        assert.equal(card.querySelector('img'), null); assert.equal(card.querySelector('.place-featured-badge'), null);
        assert.equal(card.querySelector('i').className, 'bi ' + iconClass);
    }
});
test('untrusted text stays text; invalid IDs, icons and action URLs cannot escape', () => {
    const { popup } = loadPopup(); const attack = '</div><script>alert("x")</script><img onerror=x>&';
    const card = popup.create({ ...place, name: attack, address: attack, categoryLabel: attack, iconClass: attack, detailsUrl: 'javascript:alert(1)' });
    assert.equal(card.querySelector('h3').textContent, attack); assert.equal(card.querySelector('p').textContent, attack);
    assert.equal(card.querySelector('script'), null); assert.equal(card.querySelectorAll('img').length, 1);
    assert.equal(card.querySelector('a').href, '/Places/Details/3');
    assert.equal(card.querySelector('i').className, 'bi bi-geo-alt-fill');
    for (const id of [0, -1, '3 onclick=x', Infinity, 1.5]) assert.equal(popup.create({ ...place, id }), null);
    for (const detailsUrl of ['/lokacije/4/wrong-place', '//evil.invalid', '/lokacije/3/name?bad=1'])
        assert.equal(popup.create({ ...place, detailsUrl }).querySelector('a').href, '/Places/Details/3');
});
test('shared logo load/error fallback is independent for each popup and retains actions', () => {
    const { popup, marker } = loadPopup(); const a = popup.create(place), b = popup.create({ ...place, id: 4 });
    marker.attachImage(a); marker.attachImage(b);
    a.querySelector('img').events.load(); b.querySelector('img').events.error();
    assert.equal(a.querySelector('.managed-place-popup__media').classList.contains('has-image'), true);
    assert.equal(b.querySelector('.managed-place-popup__media').classList.contains('has-image'), false);
    assert.equal(b.querySelector('img').hidden, true); assert.ok(b.querySelector('i')); assert.ok(b.querySelector('button'));
});
test('unsafe image URLs are rejected while Featured disclosure remains public', () => {
    const { popup } = loadPopup();
    for (const logoUrl of ['javascript:alert(1)', 'http://example.invalid/a', 'https://u:p@example.invalid/a'])
        assert.equal(popup.create({ ...place, logoUrl }).querySelector('img'), null);
    const badge = popup.create({ ...place, isCurrentlyFeatured: true }).querySelector('.place-featured-badge');
    assert.equal(badge.textContent, 'Izpostavljeno'); assert.match(badge.title, /ne pomeni priporočila/);
});
