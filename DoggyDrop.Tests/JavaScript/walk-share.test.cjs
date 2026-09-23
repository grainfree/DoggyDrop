const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { test } = require("node:test");

function element(values = {}) {
    const listeners = new Map();
    return {
        hidden: false,
        disabled: false,
        isConnected: true,
        ...values,
        addEventListener(type, callback) { listeners.set(type, callback); },
        fire(type, event = {}) { return listeners.get(type)?.(event); },
        removeAttribute(name) { delete this[name]; },
        focus() { this.focused = true; },
        scrollIntoView() {}
    };
}

async function settle() {
    await new Promise(resolve => setImmediate(resolve));
}

test("latest format wins, canvas sizes are exact, emoji initial and close are safe", async () => {
    const ids = Object.fromEntries([
        "walkShareData", "walkSharePreview", "walkShareHeading", "closeWalkShare",
        "walkShareImage", "walkShareLoading", "walkShareFeedback", "shareWalkStory",
        "retryWalkShare", "saveWalkShare", "copyWalkShareText"
    ].map(id => [id, element()]));
    ids.walkShareData.textContent = JSON.stringify({ asset: {
        dogName: "🐕Čar", distance: "4 m", duration: "30 min", date: "23.09.2026",
        highlight: null, photoUrl: null, text: "🐕Čar · 4 m"
    }, url: "https://doggydrop.app/Walks/Details/1" });
    ids.walkSharePreview.hidden = true;
    ids.walkShareLoading.hidden = true;
    const trigger = element();
    const formats = ["instagram-story", "facebook-story", "post"]
        .map((value, index) => element({ value, checked: index === 0 }));
    const pendingBlobs = [];
    const canvases = [];
    const drawnTexts = [];
    const revokedUrls = [];
    const shares = [];
    let urlNumber = 0;
    const document = {
        getElementById: id => ids[id],
        querySelectorAll: selector => selector === "[data-share-walk-story]" ? [trigger] : formats,
        createElement: () => {
            const canvas = {
                width: 0, height: 0,
                getContext: () => ({
                    beginPath() {}, moveTo() {}, quadraticCurveTo() {}, lineTo() {},
                    closePath() {}, fill() {}, fillRect() {}, stroke() {}, setLineDash() {}, arc() {},
                    bezierCurveTo() {},
                    createLinearGradient: () => ({ addColorStop() {} }),
                    measureText: text => ({ width: Array.from(text).length * 20 }),
                    fillText: text => drawnTexts.push(text)
                }),
                toBlob: callback => pendingBlobs.push(() => callback(new Blob(["png"], { type: "image/png" })))
            };
            canvases.push(canvas);
            return canvas;
        }
    };
    const windowListeners = new Map();
    const context = {
        document,
        window: {
            addEventListener: (type, callback) => windowListeners.set(type, callback),
            setTimeout,
            clearTimeout
        },
        navigator: { share: async payload => { shares.push(payload); }, canShare: () => true },
        URL: {
            createObjectURL: () => `blob:test-${++urlNumber}`,
            revokeObjectURL: url => revokedUrls.push(url)
        },
        File: class {
            constructor(parts, name, options) { this.parts = parts; this.name = name; this.type = options.type; }
        },
        Blob,
        Image: class { constructor() { throw new Error("No photo should load"); } }
    };
    const script = fs.readFileSync(path.join(__dirname, "../../DoggyDrop/wwwroot/js/walk-share.js"), "utf8");
    vm.runInNewContext(script, context);

    trigger.fire("click");
    await settle();
    assert.equal(canvases.length, 1);
    assert.equal(canvases[0].width, 1080);
    assert.equal(canvases[0].height, 1920);
    assert.ok(drawnTexts.includes("🐕"));

    formats[0].checked = false;
    formats[2].checked = true;
    formats[2].fire("change");
    formats[2].checked = false;
    formats[1].checked = true;
    formats[1].fire("change");
    pendingBlobs.shift()();
    await settle();
    assert.equal(canvases.length, 2);
    assert.equal(canvases[1].height, 1920);
    assert.equal(ids.shareWalkStory.disabled, true);
    pendingBlobs.shift()();
    await settle();
    assert.equal(ids.shareWalkStory.disabled, false);
    await ids.shareWalkStory.fire("click");
    assert.equal(shares[0].files[0].name, "doggydrop-facebook-story.png");

    formats[1].checked = false;
    formats[2].checked = true;
    formats[2].fire("change");
    await settle();
    assert.equal(canvases[2].height, 1350);
    assert.ok(revokedUrls.includes("blob:test-1"));
    pendingBlobs.shift()();
    await settle();
    ids.closeWalkShare.fire("click");
    assert.equal(ids.walkSharePreview.hidden, true);
    assert.equal(trigger.focused, true);
    assert.ok(revokedUrls.includes("blob:test-2"));
});
