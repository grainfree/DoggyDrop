// Captured synthetic Razor pages only. Every request is fulfilled locally or aborted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../..'),web=path.join(root,'DoggyDrop/wwwroot');
const capture=process.env.DOGGYDROP_POPUP_CAPTURE,assets=process.env.LEAFLET_TEST_ASSETS,out=process.env.LEGACY_POI_RESULTS;
if(!capture||!assets||!out)throw Error('Set DOGGYDROP_POPUP_CAPTURE, LEAFLET_TEST_ASSETS and LEGACY_POI_RESULTS.');
fs.mkdirSync(out,{recursive:true});
const tile='<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256"><rect width="256" height="256" fill="#edf2e7"/></svg>';
(async()=>{
 const browser=await chromium.launch({channel:process.env.BROWSER_CHANNEL||'msedge',headless:true}),results=[];
 try {
  for(const width of [320,375,390,430,1024,1440])for(const empty of [false,true]){
   const page=await browser.newPage({viewport:{width,height:900}}),errors=[];
   page.on('pageerror',e=>errors.push(e.message));
   await page.addInitScript(()=>{
    localStorage.setItem('doggydrop.homeIntroDismissed.v1','true');
    Object.defineProperty(navigator,'geolocation',{value:{getCurrentPosition:()=>{},watchPosition:()=>1,clearWatch:()=>{}}});
   });
   await page.route('**/*',r=>{
    const u=new URL(r.request().url());
    if(u.hostname==='unpkg.com')return r.fulfill({path:assets+(u.pathname.endsWith('.css')?'/leaflet.css':'/leaflet.js')});
    if(u.hostname==='cdn.jsdelivr.net')return r.fulfill({path:assets+(u.pathname.endsWith('.woff2')?'/bootstrap-icons.woff2':'/bootstrap-icons.css')});
    if(u.hostname.endsWith('tile.openstreetmap.org'))return r.fulfill({contentType:'image/svg+xml',body:tile});
    if(u.hostname==='127.0.0.1'){
     if(u.pathname==='/')return r.fulfill({contentType:'text/html; charset=utf-8',body:fs.readFileSync(capture+`/home-${empty?'empty':'current'}-pois.html`,'utf8')});
     if(u.pathname.startsWith('/api/'))return r.fulfill({contentType:'application/json',body:JSON.stringify({dogs:[],hotspots:[],events:[],items:[],activeWalkers:0,popularParks:0,trendingRoutes:0})});
     const file=path.resolve(web,'.'+decodeURIComponent(u.pathname));
     if(file.startsWith(web+path.sep)&&fs.existsSync(file))return r.fulfill({path:file});
    }
    return r.abort();
   });
   await page.goto('http://127.0.0.1/');
   await page.waitForFunction(()=>typeof map!=='undefined'&&map&&typeof getExploreItems==='function');
   const rows=await page.evaluate(()=>getExploreItems());
   assert.equal(rows.length,empty?0:4);
   assert.ok(rows.every(r=>r.type==='bin'||r.key.startsWith('place-')));
   assert.equal(await page.locator('.map-place-marker').count(),0);
   assert.equal(await page.evaluate(()=>managedPlaceLayer.getLayers().length),empty?0:3);
   for(const state of ['all','dog-park','dog-friendly-cafe','search-empty']){
    await page.evaluate(state=>{
     openExplorePanel(); exploreFilter=state==='search-empty'?'all':state;
     document.getElementById('exploreSearch').value=state==='search-empty'?'retired water fixture':'';renderExploreList();
    },state);
    const count=await page.locator('#exploreList .map-explore-item').count();
    assert.equal(count,empty||state==='search-empty'?0:state==='all'?4:1);
    await page.locator('#explorePanel').waitFor({state:'visible'});
    const metrics=await page.locator('#explorePanel').evaluate(el=>{const r=el.getBoundingClientRect();return {left:r.left,right:r.right,overflow:document.documentElement.scrollWidth>innerWidth+1};});
    assert.ok(metrics.left>=-1&&metrics.right<=width+1,JSON.stringify({width,state,metrics}));assert.equal(metrics.overflow,false);
    if([390,1440].includes(width)&&state==='all')await page.screenshot({path:`${out}/explore-${width}-${empty?'empty':'current'}.png`});
    results.push({width,empty,state,result:'PASS'});
   }
   if(!empty){
    await page.evaluate(()=>{map.removeLayer(managedPlaceLayer);document.getElementById('showPlaces').checked=false;focusExploreItem('place-4');});
    await page.locator('.managed-place-popup').waitFor();
    assert.equal(await page.evaluate(()=>map.hasLayer(managedPlaceLayer)&&document.getElementById('showPlaces').checked),true);
    assert.match(await page.locator('.managed-place-popup').innerText(),/Park/);
   }
   await page.evaluate(()=>{closeExplorePanel();userLocation={lat:46,lng:15};renderNearbySuggestions();document.getElementById("nearbySuggestionsPanel").hidden=false;});
   assert.equal(await page.locator('#nearbySuggestionsList [onclick*="focusExploreItem"]').count(),empty?0:3);
   assert.doesNotMatch(await page.locator('#nearbySuggestionsList').innerText(),/Pitnik|Dog friendly Lent/);
   results.push({width,empty,state:'nearby',result:'PASS'});
   assert.deepEqual(errors,[]);
   if([390,1440].includes(width))await page.screenshot({path:`${out}/home-${width}-${empty?'empty':'current'}.png`});
   await page.close();
  }
  fs.writeFileSync(out+'/results.json',JSON.stringify(results,null,2));console.log(`PASS ${results.length}/${results.length} Home/Explore/Nearby browser cases`);
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
