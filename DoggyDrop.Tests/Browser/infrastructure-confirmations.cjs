// Actual captured Home Razor/JS + real Leaflet/plugin. All requests fulfilled offline or aborted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const web=path.resolve(__dirname,'../../DoggyDrop/wwwroot');
const capture=process.env.DOGGYDROP_POPUP_CAPTURE,assets=process.env.LEAFLET_TEST_ASSETS,out=process.env.CONFIRMATION_RESULTS;
 if(!capture||!assets||!out)throw Error('Set DOGGYDROP_POPUP_CAPTURE, LEAFLET_TEST_ASSETS, CONFIRMATION_RESULTS; see docs/community-confirmations.md');
fs.mkdirSync(out,{recursive:true});
const tile='<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256"><rect width="256" height="256" fill="#edf2e7"/><path d="M0 110H256M130 0V256" stroke="white" stroke-width="8"/></svg>';
const logo='<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80"><rect width="80" height="80" fill="#ffcf32"/><text x="40" y="46" text-anchor="middle" font-size="18">PET</text></svg>';
function dataset(count) {
 const centers=[[46.0569,14.5058],[46.5547,15.6459],[45.548,13.729],[46.238,14.355],[46.235,15.267],[45.8,15.17],[45.96,13.64],[46.365,14.11]];
 const bins=[],places=[];
 for(let i=0;i<count;i++){
  const c=centers[i%8],j=Math.floor(i/8),lat=c[0]+((j*37)%97-48)*.0003,lon=c[1]+((j*53)%89-44)*.00045;
  const p={id:i+1,name:'Testna lokacija '+(i+1),latitude:lat,longitude:lon};
  if(i%5){bins.push({...p,status:'ok',reliabilityScore:60});}
  else places.push({...p,categoryKey:i%10?'dog-park':'pet-shop',categoryLabel:i%10?'Pasji park':'Trgovina',iconClass:i%10?'dd-place-icon--dog-park':'dd-place-icon--pet-shop',isCommercial:i%10===0,isCurrentlyFeatured:i%20===0,logoUrl:i%10===0?'https://fixture.invalid/logo.svg':null,detailsUrl:'/Places/Details/'+p.id});
 }
 // A repeatable nearby/identical-point case with logo and Featured semantics.
 bins.slice(0,32).forEach((b,i)=>{b.latitude=46.0569+(i%8-4)*.0003;b.longitude=14.5058+(Math.floor(i/8)-2)*.0006;});
 if(places.length>2){places[0].latitude=46.0569;places[0].longitude=14.5058;places[1].latitude=46.0569;places[1].longitude=14.5058;places[1].isCommercial=true;places[1].logoUrl='https://fixture.invalid/broken.svg';}
 return {bins,places};
}
async function load(browser,{count=250,width=390,dpr=1,provider='osm',query='',active=false,tileFailure=false,waterCount=1,adminScene=null,signedIn=true}={}){
 const page=await browser.newPage({viewport:{width,height:width<=430?844:900},deviceScaleFactor:dpr}),errors=[];
 page.on('pageerror',e=>{errors.push(e.message);console.error(e.stack);});
 const data=dataset(count);data.bins.forEach(b=>{b.name='Koš <img onerror=alert(1)>';b.sourceName='OpenStreetMap';});
 data.water=Array.from({length:waterCount},(_,i)=>({id:i+1,name:i===0?'<script>hostile</script>':'Pitnik '+i,latitude:46.0569+(i%50)*.0001,longitude:14.5058+Math.floor(i/50)*.0001,seasonality:0,access:0,dogAccess:0,sourceName:'OSM',sourceUrl:'https://openstreetmap.org'}));
 let html=fs.readFileSync(capture+'/'+(adminScene|| (active?'home-active':provider==='carto'?'home-carto':'home-anonymous'))+'.html','utf8');
 html=html.replaceAll('env(safe-area-inset-bottom)', '34px').replaceAll('env(safe-area-inset-bottom, 0px)', '34px');
 html=html.replace(/signedIn: (true|false)/, 'signedIn: '+signedIn);
 html=html.replace(/let waterPoints = [^\r\n]+;/,'let waterPoints = '+JSON.stringify(data.water).replaceAll('<','\\u003c')+';').replace(/const bins = [^\r\n]+;/,'const bins = '+JSON.stringify(data.bins)+';')
   .replace(/const managedPlaces = \([^\r\n]+\)\.filter/,'const managedPlaces = ('+JSON.stringify(data.places)+').filter')
   .replace(/<strong>\d+<\/strong>(\s*<span>pasjih košev na zemljevidu)/,'<strong>'+data.bins.length+'</strong>$1');
 await page.addInitScript(()=>{
  localStorage.setItem('doggydrop.homeIntroDismissed.v1','true');localStorage.setItem('pwaPromptShown','true');
  window.confirmationLocations=[];
  Object.defineProperty(navigator,'geolocation',{value:{getCurrentPosition:(ok,fail)=>window.confirmationLocations.push({ok,fail}),watchPosition:()=>1,clearWatch:()=>{}}});
 });
 await page.route('**/*',r=>{
  const u=new URL(r.request().url());
  if(u.hostname==='unpkg.com')return r.fulfill({path:assets+(u.pathname.endsWith('.css')?'/leaflet.css':'/leaflet.js')});
  if(u.hostname==='cdn.jsdelivr.net')return r.fulfill({path:assets+(u.pathname.endsWith('.woff2')?'/bootstrap-icons.woff2':'/bootstrap-icons.css')});
  if(u.hostname==='fixture.invalid')return r.fulfill({status:u.pathname.includes('broken')?404:200,contentType:'image/svg+xml',body:u.pathname.includes('broken')?'':logo});
  if(u.hostname==='basemaps.cartocdn.com'&&tileFailure)return r.fulfill({status:429,body:''});
  if(u.hostname.endsWith('tile.openstreetmap.org')||u.hostname==='basemaps.cartocdn.com')return r.fulfill({contentType:'image/svg+xml',body:tile});
  if(u.hostname==='127.0.0.1'){
   if(u.pathname==='/')return r.fulfill({contentType:'text/html; charset=utf-8',body:html});
   if(u.pathname==='/api/walking-route')return r.fulfill({json:{points:[[46.0569,14.5058],[46.058,14.506]],distanceMeters:200,durationSeconds:160}});
   if(u.pathname.startsWith('/api/confirmations/'))return r.fulfill({json:{outcome:'far'}});
   if(u.pathname==='/api/dogs/nearby')return r.fulfill({json:[]});
   if(u.pathname==='/Map/GetBestBin')return r.fulfill({json:data.bins[0]});
   if(u.pathname==='/api/waterpoints')return r.fulfill({json:data.water});
   if(/^\/api\/waterpoints\/\d+\/route$/.test(u.pathname))return r.fulfill({json:{points:[[46.0569,14.5058],[46.057,14.506]],distanceMeters:100,durationSeconds:80}});
   if(u.pathname.startsWith('/api/waterpoints/'))return r.fulfill({json:data.water.find(p=>p.id===Number(u.pathname.split('/').pop()))});
   if(u.pathname.startsWith('/api/'))return r.fulfill({json:{hotspots:[],events:[],items:[],activeWalkers:0,popularParks:0,trendingRoutes:0}});
   const f=path.resolve(web,'.'+decodeURIComponent(u.pathname));
   if(f.startsWith(web+path.sep)&&fs.existsSync(f)&&fs.statSync(f).isFile())return f.endsWith('.css')?r.fulfill({contentType:'text/css',body:fs.readFileSync(f,'utf8').replaceAll('env(safe-area-inset-bottom)', '34px').replaceAll('env(safe-area-inset-bottom, 0px)', '34px')}):r.fulfill({path:f});
  }
  return r.abort();
 });
 const start=performance.now();await page.goto('http://127.0.0.1/'+query);
 if(!adminScene)await page.waitForFunction(()=>typeof homeLocations!=='undefined'&&homeLocations&&binLayer&&managedPlaceLayer&&waterLayer);
 await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
 await page.evaluate(()=>{window.confirmationLocations=[];});
 return {page,errors,initialMs:performance.now()-start,data};
}
async function counts(page){return page.evaluate(()=>{
 const groups=[];map.eachLayer(l=>{if(l instanceof L.MarkerClusterGroup)groups.push(l);});
 const group=groups[0],logical=[...(map.hasLayer(binLayer)?binLayer.getLayers():[]),...(map.hasLayer(managedPlaceLayer)?managedPlaceLayer.getLayers():[]),...(map.hasLayer(waterLayer)?waterLayer.getLayers():[])];
 const grouped=group.getLayers(),pinned=logical.filter(m=>!group.hasLayer(m)&&map.hasLayer(m));
 const ids=[...grouped,...pinned].map(L.stamp);
 const clusters=[...document.querySelectorAll('.home-location-cluster')];
 return {groups:groups.length,expected:logical.length,count:ids.length,unique:new Set(ids).size,pinned:pinned.length,
  dom:document.querySelectorAll('.leaflet-marker-icon').length,clusters:clusters.length,
  labels:clusters.every(e=>e.getAttribute('aria-label')?.startsWith(e.textContent.trim()+' ')&&e.tabIndex===0&&e.getAttribute('role')==='button'),
  centered:clusters.every(e=>{const a=e.getBoundingClientRect(),b=e.firstChild.getBoundingClientRect();return Math.abs((a.left+a.right-b.left-b.right)/2)<2&&Math.abs((a.top+a.bottom-b.top-b.bottom)/2)<2;}),
  clusterSum:clusters.reduce((a,e)=>a+Number(e.textContent),0),
  overflow:document.documentElement.scrollWidth>innerWidth+1};
});}
function conserve(c){assert.equal(c.groups,1);assert.equal(c.count,c.expected);assert.equal(c.unique,c.count);assert.ok(c.labels);assert.ok(c.centered,'cluster text must be centered');}


async function open(page,kind,summary=null){
 await page.evaluate(({kind,summary})=>{
  DoggyDropConfirmations.cancel();closeBinDetail();map.closePopup();
  if(kind==='bin'){bins[0].trust=summary;openBinDetail(bins[0]);}
  else if(navigation.active){waterPoints[0].trust=summary;L.popup({...waterMarkers.get(1).getPopup().options}).setLatLng([waterPoints[0].latitude,waterPoints[0].longitude]).setContent(()=>DoggyDropWaterPoints.popup(waterPoints[0],()=>{})).openOn(map);}
  else {waterPoints[0].trust=summary;map.setView([waterPoints[0].latitude,waterPoints[0].longitude],18,{animate:false});homeLocations.reveal(waterMarkers.get(1),()=>waterMarkers.get(1).openPopup());}
 },{kind,summary});
 await page.waitForFunction(kind=>!!document.querySelector(`[data-confirm-kind="${kind}"] .confirmation-action`),kind);
 if(kind==='water')await page.waitForFunction(()=>[...document.querySelectorAll('.leaflet-popup')].some(p=>getComputedStyle(p).opacity==='1'));
 return page.locator(`[data-confirm-kind="${kind}"]`).filter({visible:true}).first();
}
async function layout(page,node,width){
 const result=await node.evaluate(el=>{
  const button=el.querySelector('.confirmation-action');button.scrollIntoView({block:'nearest'});
  const b=button.getBoundingClientRect(),nav=document.querySelector('.app-bottom-nav')?.getBoundingClientRect();
  const card=document.body.classList.contains('map-bin-navigation-active')?document.getElementById('directionsPanel').getBoundingClientRect():null;
  const attribution=document.querySelector('.leaflet-control-attribution').getBoundingClientRect();
  const surface=el.closest('.map-bin-detail__panel,.leaflet-popup').getBoundingClientRect();
  return {width:document.documentElement.scrollWidth,viewport:innerWidth,button:{x:b.x,y:b.y,width:b.width,height:b.height,bottom:b.bottom},navTop:Math.min(nav?.height?nav.top:innerHeight,card?.height?card.top:innerHeight),source:document.body.textContent.includes('OpenStreetMap'),attributionClear:surface.bottom<=attribution.top||surface.top>=attribution.bottom||surface.right<=attribution.left||surface.left>=attribution.right};
 });
 assert.ok(result.width<=width+1,JSON.stringify(result));assert.ok(result.button.width>30&&result.button.height>=43.9,JSON.stringify(result));
 assert.ok(result.button.x>=0&&result.button.x+result.button.width<=width+1,JSON.stringify(result));
 assert.ok(result.button.bottom<=result.navTop+1,JSON.stringify(result));assert.ok(result.source);assert.ok(result.attributionClear,JSON.stringify(result));return result;
}
(async()=>{
 const browser=await chromium.launch({channel:process.env.BROWSER_CHANNEL||'msedge',headless:true});const layouts=[],flows=[],adminLayouts=[];
 try{
  for(const width of [320,375,390,430,768,1024,1440]){
   const {page,errors}=await load(browser,{width,count:50});
   await page.emulateMedia({reducedMotion:'reduce'});
   for(const kind of ['bin','water']){
    for(const state of ['unconfirmed','recent','multiple','old','issue']){
     const summary=state==='unconfirmed'?null:{lastConfirmedAt:new Date(Date.now()-(state==='old'?220:1)*86400000).toISOString(),recentUniqueConfirmers:state==='multiple'?3:state==='old'?0:1,state:state==='old'?'stale':state,hasCurrentIssue:state==='issue'};
     const node=await open(page,kind,summary);layouts.push({width,kind,state,...await layout(page,node,width)});
     assert.equal(await node.locator('script,img').count(),0);
     if(width===390&&['unconfirmed','recent','issue'].includes(state))await page.screenshot({path:path.join(out,`${kind}-${state}-390.png`)});
    }
    for(const outcome of ['far','accuracy','accepted','cooldown']){
     let requests=0;await page.route('**/api/confirmations/**',route=>{requests++;return route.fulfill({json:{outcome,...(['accepted','cooldown'].includes(outcome)?{summary:{lastConfirmedAt:new Date().toISOString(),recentUniqueConfirmers:2,state:'recent',hasCurrentIssue:false}}:{})}});});
     const node=await open(page,kind);const button=node.locator('button');await button.focus();await page.keyboard.press('Enter');
     assert.equal(await button.isDisabled(),true);assert.equal(await node.getAttribute('aria-busy'),'true');assert.match(await button.textContent(),/Preverjam/);
     await page.evaluate(()=>document.querySelector('[data-confirm-action]:disabled')?.click());
     assert.equal(await page.evaluate(()=>window.confirmationLocations.length),1);
     await page.evaluate(()=>window.confirmationLocations.shift().ok({coords:{latitude:46.0569,longitude:14.5058,accuracy:5}}));
     await page.waitForFunction(kind=>document.querySelector(`[data-confirm-kind="${kind}"]`)?.getAttribute('aria-busy')==='false',kind);
     assert.equal(requests,1);assert.equal(await button.isDisabled(),false);assert.ok((await node.locator('[role=status]').textContent()).length>5);
     layouts.push({width,kind,state:outcome,...await layout(page,node,width)});flows.push({width,kind,outcome,requests});
     if(width===390&&['far','accuracy'].includes(outcome))await page.screenshot({path:path.join(out,`${kind}-${outcome}-390.png`)});
     await page.unroute('**/api/confirmations/**');
    }
    // Closing the UI cancels intent before a late GPS callback; no request is sent.
    let requests=0;await page.route('**/api/confirmations/**',route=>{requests++;return route.abort();});
    const node=await open(page,kind);await node.locator('button').click();await page.evaluate(()=>{closeBinDetail();map.closePopup();window.confirmationLocations.shift().ok({coords:{latitude:46,longitude:15,accuracy:5}});});
    await page.evaluate(()=>new Promise(r=>requestAnimationFrame(r)));assert.equal(requests,0);flows.push({width,kind,outcome:'closed-late-location',requests});await page.unroute('**/api/confirmations/**');
   }
   for(const kind of ['bin','water']){
    const style=await page.addStyleTag({content:'.infrastructure-confirmation p,.infrastructure-confirmation small,.infrastructure-confirmation .confirmation-action {font-size:20px !important;line-height:1.7 !important;}'});
    const large=await open(page,kind);layouts.push({width,kind,state:'enlarged-text',...await layout(page,large,width)});await style.evaluate(el=>el.remove());
   }
   await page.evaluate(()=>{userLocation={lat:46.0569,lng:14.5058};startInAppBinNavigation(bins[0].id,bins[0]);});
   await page.waitForFunction(()=>navigation.routeStatus==='routed');
   await page.waitForFunction(()=>!map._animatingZoom&&!map._panAnim?._inProgress);
   // Navigation deliberately hides public layers. Stress-test the actual popup renderer
   // above the card without changing cluster membership or navigation behavior.
   for(const kind of ['bin','water']){const node=await open(page,kind);layouts.push({width,kind,state:'active-navigation',...await layout(page,node,width)});}
   await page.evaluate(()=>stopInAppNavigation());
   assert.deepEqual(errors,[]);await page.close();
   const anon=await load(browser,{width,count:50,signedIn:false});for(const kind of ['bin','water']){const node=await open(anon.page,kind);assert.equal(await node.locator('button').count(),0);assert.match(await node.locator('a').getAttribute('href'),/^\/Identity\/Account\/Login/);assert.equal(await anon.page.evaluate(()=>confirmationLocations.length),0);layouts.push({width,kind,state:'anonymous',...await layout(anon.page,node,width)});}await anon.page.close();
  }
  for(const width of [320,375,390,430,768,1024,1440]){
   const {page,errors}=await load(browser,{width,adminScene:'admin-confirmations'});
   await page.getByRole('heading',{name:'Potrditve skupnosti'}).waitFor();
   assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);
   await page.getByRole('link',{name:'Nazaj na seznam'}).focus();assert.deepEqual(errors,[]);
   adminLayouts.push({width});await page.close();
  }
  fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({layouts,flows,adminLayouts},null,2));console.log(JSON.stringify({layouts:layouts.length,flows:flows.length,adminLayouts:adminLayouts.length}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
