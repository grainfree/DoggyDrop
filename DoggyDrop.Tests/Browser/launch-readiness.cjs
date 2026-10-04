// Loopback HTTP + actual Razor captures. All third-party traffic is fulfilled locally or aborted.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const web=path.resolve(__dirname,'../../DoggyDrop/wwwroot'),capture=process.env.DOGGYDROP_POPUP_CAPTURE,assets=process.env.LEAFLET_TEST_ASSETS,out=process.env.LAUNCH_RESULTS;
if(!capture||!assets||!out)throw Error('Set DOGGYDROP_POPUP_CAPTURE, LEAFLET_TEST_ASSETS, LAUNCH_RESULTS');
fs.mkdirSync(out,{recursive:true});
const home=fs.readFileSync(path.join(capture,'home-anonymous.html'),'utf8').replace(/const bins = [^\r\n]+;/,'const bins = '+JSON.stringify([{id:1,name:"Synthetic bin",latitude:46.057,longitude:14.506,status:"ok",reliabilityScore:60}])+';');
let workerVersion=1,posts=0,user='A';
const types={'.js':'application/javascript','.css':'text/css','.html':'text/html','.json':'application/json','.png':'image/png','.svg':'image/svg+xml','.woff2':'font/woff2'};
const server=http.createServer((req,res)=>{
 const url=new URL(req.url,'http://localhost');
 res.setHeader('Cache-Control','no-store');res.setHeader('Content-Security-Policy',"base-uri 'self'; object-src 'none'; frame-ancestors 'none'");res.setHeader('X-Content-Type-Options','nosniff');
 if(req.method==='POST'){posts++;res.end('synthetic accepted');return;}
 if(url.pathname==='/'){res.setHeader('Content-Type','text/html');res.end(home);return;}
 if(url.pathname==='/sw.js'){res.setHeader('Content-Type','application/javascript');res.end(fs.readFileSync(path.join(web,'sw.js'),'utf8').replace('doggydrop-offline-v1','doggydrop-offline-v'+workerVersion));return;}
 if(url.pathname==='/Map/GetBestBin'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify({id:1,name:'Synthetic bin',latitude:46.057,longitude:14.506}));return;}
 if(url.pathname.startsWith('/private/')){res.setHeader('Content-Type','text/html');res.end('<!doctype html><html lang="sl"><body><main>PRIVATE_USER_'+user+'</main><input id="unsaved" value="local draft"></body></html>');return;}
 const file=path.resolve(web,'.'+url.pathname);
 if(file.startsWith(web+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile()){res.setHeader('Content-Type',types[path.extname(file)]||'application/octet-stream');res.end(fs.readFileSync(file));return;}
 res.setHeader('Content-Type','application/json');res.end('{}');
});
(async()=>{
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const origin='http://127.0.0.1:'+server.address().port;
 const browser=await chromium.launch({channel:process.env.BROWSER_CHANNEL||'msedge',headless:true});
 const layouts=[],flows=[];
 async function context(options={}){
  const c=await browser.newContext(options);
  await c.route('**/*',route=>{const u=new URL(route.request().url());
   if(u.origin===origin)return route.continue();
   if(u.hostname==='unpkg.com')return route.fulfill({path:path.join(assets,u.pathname.endsWith('.css')?'leaflet.css':'leaflet.js')});
   if(u.hostname==='cdn.jsdelivr.net')return route.fulfill({path:path.join(assets,u.pathname.endsWith('.woff2')?'bootstrap-icons.woff2':'bootstrap-icons.css')});
   if(/tile.openstreetmap.org|basemaps.cartocdn.com|tiles.stadiamaps.com/.test(u.hostname))return route.fulfill({contentType:'image/svg+xml',body:'<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256"><rect width="256" height="256" fill="#edf2e7"/></svg>'});
   return route.abort();
  });
  await c.addInitScript(()=>{window.geoCalls=0;Object.defineProperty(navigator,'geolocation',{value:{getCurrentPosition(){window.geoCalls++;},watchPosition(){window.geoCalls++;return 1;},clearWatch(){}}});});
  return c;
 }
 async function clear(page,selector){const e=page.locator(selector);await e.scrollIntoViewIfNeeded();await e.focus();assert.equal(await e.evaluate(x=>document.activeElement===x),true);assert.equal(await e.evaluate(x=>{const r=x.getBoundingClientRect();const nav=document.querySelector('.app-bottom-nav');const top=nav&&getComputedStyle(nav).display!=='none'?nav.getBoundingClientRect().top:innerHeight;return r.top>=0&&r.bottom<=top+1;}),true,'clearance '+selector);}
 try{
  for(const width of [320,375,390,430,768,1024,1440])for(const height of [900,430])for(const safe of [0,34])for(const scale of [1,1.25]){
   console.log('Layout',JSON.stringify({width,height,safe,scale}));
   const c=await context({viewport:{width,height},reducedMotion:'reduce'}),page=await c.newPage();
   await page.goto(origin);await page.waitForSelector('#homeIntro:not([hidden])');
   await page.addStyleTag({content:`html{font-size:${scale*100}%}:root{--app-bottom-nav-clearance:${88+safe}px}.app-bottom-nav{bottom:${12+safe}px}`});
   assert.equal(await page.evaluate(()=>window.geoCalls),0);
   assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
   await clear(page,'#dismissHomeIntro');await page.click('#dismissHomeIntro');
   assert.equal(await page.evaluate(()=>localStorage.getItem('doggydrop.homeIntroDismissed.v1')),'true');
   await page.reload();assert.equal(await page.locator('#homeIntro').isVisible(),false);assert.equal(await page.evaluate(()=>window.geoCalls),0);
   await page.click('[data-bs-target="#appMenu"]');await page.locator('#appMenu').evaluate(e=>e.classList.contains('show'));
   await page.locator('#pwaInstall summary').click();await clear(page,'#pwaInstall summary');assert.ok(await page.locator('#pwaInstallHelp').isVisible());
   if(width===390&&height===900&&safe===34&&scale===1.25)await page.screenshot({path:path.join(out,'install-390.png'),fullPage:true});
   layouts.push({width,height,safe,scale});await c.close();
  }
  const c=await context({viewport:{width:390,height:844}});let page=await c.newPage();await page.goto(origin);await page.evaluate(()=>navigator.serviceWorker.ready);await page.reload();
  await page.waitForFunction(()=>!!navigator.serviceWorker.controller);
  for(const code of [1,2,3]){
   const result=await page.evaluate(code=>{window.locationAlerts=[];window.alert=message=>window.locationAlerts.push(message);const before=window.geoCalls;navigator.geolocation.getCurrentPosition=(ok,bad)=>{window.geoCalls++;bad({code});};findNearestTrashBin();return {calls:window.geoCalls-before,messages:window.locationAlerts};},code);
   assert.equal(result.calls,1);assert.equal(result.messages.length,1);assert.match(result.messages[0],/Zemljevid lahko raziskuješ/);flows.push('explicit location failure '+code);
  }
  await page.evaluate(()=>{navigator.geolocation.getCurrentPosition=ok=>{window.geoCalls++;ok({coords:{latitude:46.0569,longitude:14.5058}});};findNearestTrashBin();});
  await page.waitForSelector('#nearestBinPreview:not([hidden])');flows.push('explicit location retry success');await page.evaluate(()=>closeNearestBinPreview(false));
  const viewport=await page.getAttribute('meta[name=viewport]','content');assert.doesNotMatch(viewport,/user-scalable=no|maximum-scale=1/);
  const cdp=await c.newCDPSession(page);await cdp.send('Emulation.setPageScaleFactor',{pageScaleFactor:1.25});assert.ok(await page.evaluate(()=>visualViewport.scale>1));await cdp.send('Emulation.setPageScaleFactor',{pageScaleFactor:1});flows.push('pinch zoom enabled');
  const entries=await page.evaluate(async()=>{const k=await caches.keys();return Promise.all(k.map(async x=>(await (await caches.open(x)).keys()).map(r=>new URL(r.url).pathname)));});assert.deepEqual(entries.flat().sort(),['/css/pwa.css','/offline.html']);flows.push('fresh install allowlist');
  for(const name of ['Profile','Dogs','Walks','SavedPlans','Contributions','Identity','Admin']){
   user='A';await page.goto(origin+'/private/'+name);assert.match(await page.locator('main').innerText(),/PRIVATE_USER_A/);
   user='B';await page.goto(origin+'/private/'+name);assert.match(await page.locator('main').innerText(),/PRIVATE_USER_B/);
   await c.setOffline(true);await page.reload();assert.ok(await page.getByRole('heading',{name:'Trenutno ni povezave.'}).isVisible());assert.doesNotMatch(await page.content(),/PRIVATE_USER/);
   await c.setOffline(false);await page.getByRole('link',{name:'Poskusi znova – odpri zemljevid'}).click();await page.waitForSelector('#map');flows.push('private A/B/offline '+name);
  }
  for(const name of ['password','contribution','confirmation','walk','admin']){
   await c.setOffline(true);assert.equal(await page.evaluate(async name=>{try{await fetch('/private/'+name,{method:'POST',body:'synthetic'});return false;}catch{return true;}},name),true);
   await c.setOffline(false);assert.equal(posts,0);flows.push('no mutation replay '+name);
  }
  await page.evaluate(()=>{window.installCalls=0;const event=new Event('beforeinstallprompt',{cancelable:true});event.prompt=async()=>window.installCalls++;event.userChoice=Promise.resolve({outcome:'dismissed'});window.dispatchEvent(event);});
  assert.equal(await page.evaluate(()=>window.installCalls),0);await page.click('[data-bs-target="#appMenu"]');await page.click('#pwaInstall summary');await page.click('#pwaInstallButton');assert.equal(await page.evaluate(()=>window.installCalls),1);flows.push('explicit install dismiss');
  await page.evaluate(()=>{document.body.classList.add('map-walk-active');document.body.insertAdjacentHTML('beforeend','<form aria-busy="true"><input id="draft" value="unsaved photo note"></form>');window.unchangedState='still active';});
  workerVersion=2;await page.evaluate(async()=>{const reg=await navigator.serviceWorker.getRegistration();await reg.update();});
  await page.waitForFunction(async()=>!!(await navigator.serviceWorker.getRegistration()).waiting);
  assert.equal(await page.evaluate(()=>window.unchangedState),'still active');assert.equal(await page.locator('#draft').inputValue(),'unsaved photo note');assert.ok(await page.locator('#pwaUpdate').isVisible());flows.push('waiting update preserves active walk and form');
  await page.close();page=await c.newPage();await page.goto(origin);await page.waitForFunction(async()=> (await caches.keys()).includes('doggydrop-offline-v2'));
  assert.deepEqual(await page.evaluate(()=>caches.keys()),['doggydrop-offline-v2']);flows.push('normal close/reopen activates update');
  await c.setOffline(true);await page.reload();await clear(page,'main a');await page.screenshot({path:path.join(out,'offline-390.png'),fullPage:true});flows.push('offline keyboard recovery');await c.close();
  fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({layouts:layouts.length,flows:flows.length,layoutResults:layouts,flowResults:flows},null,2));
  console.log(JSON.stringify({layouts:layouts.length,flows:flows.length}));
 }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
})().catch(e=>{console.error(e);process.exitCode=1;server.close();});
