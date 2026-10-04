// Actual local Razor captures, with every browser request intercepted; no identity provider calls.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const { chromium } = require('playwright');
const capture = process.env.DOGGYDROP_IDENTITY_CAPTURE, out = process.env.DOGGYDROP_IDENTITY_RESULTS;
const assets = process.env.LEAFLET_TEST_ASSETS;
if (!capture || !out) throw Error('Set external capture/results paths outside the repository');
const web = path.resolve(__dirname, '../../DoggyDrop/wwwroot');
fs.mkdirSync(out, { recursive: true });
(async () => {
    const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL || 'msedge', headless: true });
    const results = [];
    try {
        for (const width of [375,390,430,1024,1440])
        for (const height of [900,420])
        for (const safeArea of [0,34])
        for (const scene of ['email-change-success','email-change-error']) {
            const page = await browser.newPage({ viewport: { width, height } }), errors = [];
            page.on('pageerror', e => errors.push(e.message));
            await page.addInitScript(() => localStorage.setItem('pwaPromptShown','true'));
            await page.route('**/*', route => {
                const url = new URL(route.request().url());
                if (assets && url.hostname === 'cdn.jsdelivr.net')
                    return route.fulfill({path:path.join(assets,url.pathname.endsWith('.woff2')?'bootstrap-icons.woff2':'bootstrap-icons.css')});
                if (url.hostname !== '127.0.0.1') return route.abort();
                if (url.pathname === '/') return route.fulfill({contentType:'text/html; charset=utf-8',body:fs.readFileSync(path.join(capture,scene+'.html'),'utf8')});
                if (url.pathname.startsWith('/api/')) return route.fulfill({json:{items:[],unreadCount:0}});
                const file=path.resolve(web,'.'+url.pathname);
                if(file.startsWith(web+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});
                return route.abort();
            });
            await page.goto('http://127.0.0.1/');
            if(safeArea)await page.addStyleTag({content:':root{--app-bottom-nav-clearance:122px}.app-bottom-nav{bottom:46px}'});
            assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'horizontal overflow');
            const scope=page.locator('main');
            assert.ok(await scope.getByRole('heading', { name: 'Potrditev spremembe e-pošte', exact:true }).isVisible());
            assert.ok(await scope.locator('[role=status]').isVisible());
            for(const control of await scope.locator('button,a,input:not([type=hidden])').all()) {
                if(!await control.isVisible())continue;
                await control.focus();assert.ok(await control.evaluate(e=>document.activeElement===e));
                await control.evaluate(e=>{
                    e.scrollIntoView({block:'center',behavior:'instant'});
                    const nav=document.querySelector('.app-bottom-nav');
                    const bottom=nav&&getComputedStyle(nav).display!=='none'?nav.getBoundingClientRect().top:innerHeight;
                    if(e.getBoundingClientRect().bottom>bottom-12)scrollBy({top:e.getBoundingClientRect().bottom-bottom+12,behavior:'instant'});
                });
                assert.ok(await control.evaluate(e=>{
                    const nav=document.querySelector('.app-bottom-nav');const bottom=nav&&getComputedStyle(nav).display!=='none'?nav.getBoundingClientRect().top:innerHeight;
                    return e.getBoundingClientRect().bottom<=bottom+1;
                }),'bottom navigation overlap');
            }
            assert.deepEqual(errors,[]);
            if(width===390&&height===900&&safeArea===34)await page.screenshot({path:path.join(out,scene+'-390.png'),fullPage:true});
            results.push({width,height,safeArea,scene});await page.close();
        }
        for (const scene of ['ResetPassword','ConfirmEmail','ConfirmEmailChange']) {
            const page=await browser.newPage(), marker='synthetic-referrer-marker';let leaked=false,requests=0;
            await page.route('**/*',route=>{
                const url=new URL(route.request().url());
                if(url.hostname==='audit.example')return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(capture,scene+'.html'),'utf8')});
                requests++;if((route.request().headers().referer||'').includes(marker))leaked=true;
                return route.abort();
            });
            await page.goto('https://audit.example/Identity/Account/'+scene+'?code='+marker);
            assert.ok(requests>0);assert.equal(leaked,false,'token URL leaked to third-party resource');
            results.push({scene,referrerPrivacy:true});await page.close();
        }
    } finally {await browser.close();}
    fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({passed:results.length,results},null,2));
    console.log(`Identity email-change browser/layout/accessibility: ${results.length}/${results.length} PASS`);
})().catch(e=>{console.error(e);process.exit(1);});
