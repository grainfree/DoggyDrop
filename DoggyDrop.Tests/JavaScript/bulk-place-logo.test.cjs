const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname, '../../DoggyDrop/wwwroot/js/bulk-place-logo.js'), 'utf8');
function setup() {
    const changes = {}, events = {}, images = {}, created = [], revoked = [];
    const input = { files: [], addEventListener: (name, fn) => changes[name] = fn };
    const image = { addEventListener: (name, fn) => images[name] = fn, removeAttribute: () => delete image.src };
    const preview = { hidden: true, querySelector: () => image };
    const urls = { createObjectURL: file => { created.push(file); return 'blob:local/' + created.length; }, revokeObjectURL: url => revoked.push(url) };
    vm.runInNewContext(script, { document: { getElementById: id => id === 'bulkLogoFile' ? input : preview },
        window: { URL: urls, addEventListener: (name, fn) => events[name] = fn }, URL: urls,
        fetch: () => { throw new Error('Preview must never upload'); } });
    return { input, image, preview, created, revoked, changes, events, images };
}
test('bulk logo preview is local and replacing/clearing releases object URLs', () => {
    const s = setup();
    s.input.files = [{ type: 'image/png', size: 100 }]; s.changes.change();
    assert.equal(s.preview.hidden, false); assert.equal(s.image.src, 'blob:local/1');
    s.input.files = [{ type: 'image/webp', size: 200 }]; s.changes.change();
    assert.deepEqual(s.revoked, ['blob:local/1']); assert.equal(s.image.src, 'blob:local/2');
    s.input.files = []; s.changes.change();
    assert.deepEqual(s.revoked, ['blob:local/1', 'blob:local/2']); assert.equal(s.preview.hidden, true);
});
test('bulk logo preview rejects SVG and oversized input without creating URLs', () => {
    const s = setup();
    for (const file of [{ type: 'image/svg+xml', size: 20 }, { type: 'image/png', size: 5 * 1024 * 1024 + 1 }]) {
        s.input.files = [file]; s.changes.change(); assert.equal(s.preview.hidden, true);
    }
    assert.equal(s.created.length, 0);
});
test('bulk logo decode failure and page exit release the local preview', () => {
    const s = setup(); s.input.files = [{ type: 'image/jpeg', size: 100 }]; s.changes.change(); s.images.error();
    assert.equal(s.preview.hidden, true); assert.equal(s.revoked.length, 1);
    s.changes.change(); s.events.pagehide(); assert.equal(s.revoked.length, 2); assert.equal(s.image.src, undefined);
});
test('bulk logo enhancement is optional on other pages', () => {
    assert.doesNotThrow(() => vm.runInNewContext(script, { document: { getElementById: () => null }, window: {} }));
});
