// Actual local Razor captures; all browser requests are intercepted. No external services.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const { chromium } = require('playwright');
const capture = process.env.DOGGYDROP_PASSWORD_CAPTURE, out = process.env.DOGGYDROP_PASSWORD_RESULTS;
const assets = process.env.LEAFLET_TEST_ASSETS;
if (!capture || !out) throw Error('Set DOGGYDROP_PASSWORD_CAPTURE and DOGGYDROP_PASSWORD_RESULTS outside the repository');
const web = path.resolve(__dirname, '../../DoggyDrop/wwwroot');
fs.mkdirSync(out, { recursive: true });
(async () => {
    const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL || 'msedge', headless: true });
    const results = [];
    try {
        for (const width of [320, 375, 390, 430, 768, 1024, 1440])
        for (const height of [900, 360])
        for (const safeArea of [0, 34])
        for (const scene of ['change', 'errors', 'policy-errors', 'success', 'set', 'set-errors']) {
            const page = await browser.newPage({ viewport: { width, height } }), errors = [];
            page.on('pageerror', e => errors.push(e.message));
            await page.addInitScript(() => localStorage.setItem('pwaPromptShown', 'true'));
            await page.route('**/*', route => {
                const url = new URL(route.request().url());
                if (assets && url.hostname === 'cdn.jsdelivr.net')
                    return route.fulfill({ path: path.join(assets, url.pathname.endsWith('.woff2') ? 'bootstrap-icons.woff2' : 'bootstrap-icons.css') });
                if (url.hostname !== '127.0.0.1') return route.abort();
                if (url.pathname === '/') return route.fulfill({ contentType: 'text/html; charset=utf-8', body: fs.readFileSync(path.join(capture, scene + '.html'), 'utf8') });
                if (url.pathname.startsWith('/api/')) return route.fulfill({ json: { items: [], unreadCount: 0 } });
                const file = path.resolve(web, '.' + url.pathname);
                if (file.startsWith(web + path.sep) && fs.existsSync(file) && fs.statSync(file).isFile()) return route.fulfill({ path: file });
                return route.abort();
            });
            await page.goto('http://127.0.0.1/');
            // Model an iPhone bottom inset; retain the actual navigation and application clearance convention.
            if (safeArea) await page.addStyleTag({ content: ':root{--app-bottom-nav-clearance:122px}.app-bottom-nav{bottom:46px}' });
            const scope = page.locator('.account-security');
            assert.equal(await scope.locator('h1').count(), 1);
            assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'horizontal overflow');
            for (const input of await scope.locator('input[type=password]').all()) {
                const id = await input.getAttribute('id');
                assert.equal(await scope.locator(`label[for="${id}"]`).count(), 1);
                assert.ok(['current-password', 'new-password'].includes(await input.getAttribute('autocomplete')));
                assert.equal(await input.inputValue(), '');
                const errorId = await input.getAttribute('aria-describedby');
                assert.equal(await scope.locator('#' + errorId).count(), 1);
                assert.ok(await input.evaluate(e => parseFloat(getComputedStyle(e).fontSize) >= 16));
                await input.focus();
                assert.ok(await input.evaluate(e => getComputedStyle(e).outlineStyle !== 'none'));
            }
            assert.ok(await scope.locator('button').evaluate(e => e.getBoundingClientRect().height >= 44));
            if (scene.includes('errors')) assert.ok(await scope.locator('[role=alert]').isVisible());
            if (scene === 'success') assert.ok(await scope.locator('[role=status]').isVisible());
            if (width < 768) assert.ok(await page.locator('.app-bottom-nav').isVisible());
            for (const control of await scope.locator('input[type=password],button,a,.field-validation-error,[role=alert],[role=status]').all()) {
                if (!await control.isVisible()) continue;
                await control.evaluate(e => e.scrollIntoView({ block: 'center', behavior: 'instant' }));
                // Centering in the whole viewport can straddle a fixed nav in a short keyboard
                // viewport. Exercise the remaining scroll range, as a user can, before asserting.
                await control.evaluate(e => {
                    const r = e.getBoundingClientRect(), nav = document.querySelector('.app-bottom-nav');
                    const bottom = nav && getComputedStyle(nav).display !== 'none' ? nav.getBoundingClientRect().top : innerHeight;
                    if (r.bottom > bottom - 12 && r.height <= bottom - 90)
                        scrollBy({ top: r.bottom - bottom + 12, behavior: 'instant' });
                });
                const clearance = await control.evaluate(e => {
                    const r = e.getBoundingClientRect(), nav = document.querySelector('.app-bottom-nav');
                    const bottom = nav && getComputedStyle(nav).display !== 'none' ? nav.getBoundingClientRect().top : innerHeight;
                    return { clear: r.height > bottom - 90 ? r.top < bottom : r.bottom <= bottom + 1,
                        top: r.top, bottom: r.bottom, navigationTop: bottom, id: e.id, tag: e.tagName };
                });
                if (!clearance.clear) await page.screenshot({ path: path.join(out, 'clearance-failure.png'), fullPage: true });
                assert.ok(clearance.clear, JSON.stringify({ width, height, safeArea, scene, ...clearance }));
            }
            // Native keyboard submission, with test-only synthetic text and no network request.
            await page.evaluate(() => { window.testSubmits = 0; document.querySelector('.account-security form').addEventListener('submit', e => { e.preventDefault(); window.testSubmits++; }); });
            for (const input of await scope.locator('input[type=password]').all()) await input.fill('Synthetic-browser-only!7');
            await scope.locator('input[type=password]').last().press('Enter');
            assert.equal(await page.evaluate(() => window.testSubmits), 1);
            for (const input of await scope.locator('input[type=password]').all()) await input.fill('');
            assert.deepEqual(errors, []);
            if (width === 390 && height === 900 && safeArea === 34) {
                await page.evaluate(() => scrollTo(0, 0));
                await page.screenshot({ path: path.join(out, scene + '-390.png'), fullPage: true });
            }
            results.push({ width, height, safeArea, scene }); await page.close();
        }
    } finally { await browser.close(); }
    fs.writeFileSync(path.join(out, 'results.json'), JSON.stringify({ passed: results.length, results }, null, 2));
    console.log(`Password settings browser layouts/accessibility/keyboard: ${results.length}/${results.length} PASS`);
})().catch(error => { console.error(error); process.exit(1); });
