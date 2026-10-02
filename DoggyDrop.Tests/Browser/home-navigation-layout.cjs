// Actual captured Home Razor/JS + real Leaflet/plugin. All requests fulfilled offline or aborted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const web=path.resolve(__dirname,'../../DoggyDrop/wwwroot');
const capture=process.env.DOGGYDROP_POPUP_CAPTURE,assets=process.env.LEAFLET_TEST_ASSETS,out=process.env.NAVIGATION_LAYOUT_RESULTS;
if(!capture||!assets||!out)throw Error('Set DOGGYDROP_POPUP_CAPTURE, LEAFLET_TEST_ASSETS, NAVIGATION_LAYOUT_RESULTS; see docs/home-map-clustering.md');
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
async function load(browser,{count=20,width=390,height=844,dpr=1,provider='osm',query='',active=false,tileFailure=false,waterCount=500,adminScene=null}={}){
 const page=await browser.newPage({viewport:{width,height},deviceScaleFactor:dpr}),errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 const data=dataset(count);
 data.water=Array.from({length:waterCount},(_,i)=>({id:i+1,name:i===0?'<script>hostile</script>':'Pitnik '+i,latitude:46.0569+(i%50)*.0001,longitude:14.5058+Math.floor(i/50)*.0001,seasonality:0,access:0,dogAccess:0,sourceName:'OSM',sourceUrl:'https://openstreetmap.org'}));
 let html=fs.readFileSync(capture+'/'+(adminScene|| (active?'home-active':provider==='carto'?'home-carto':'home-anonymous'))+'.html','utf8');
 html=html.replace(/let waterPoints = [^\r\n]+;/,'let waterPoints = '+JSON.stringify(data.water).replaceAll('<','\\u003c')+';').replace(/const bins = [^\r\n]+;/,'const bins = '+JSON.stringify(data.bins)+';')
   .replace(/const managedPlaces = \([^\r\n]+\)\.filter/,'const managedPlaces = ('+JSON.stringify(data.places)+').filter')
   .replace(/<strong>\d+<\/strong>(\s*<span>pasjih koÄąË‡ev na zemljevidu)/,'<strong>'+data.bins.length+'</strong>$1');
 await page.addInitScript(()=>{
  localStorage.setItem('doggydrop.homeIntroDismissed.v1','true');
  localStorage.setItem('pwaPromptShown','true');
  Object.defineProperty(navigator,'geolocation',{value:{getCurrentPosition:fn=>fn({coords:{latitude:46.0569,longitude:14.5058,accuracy:10}}),watchPosition:()=>1,clearWatch:()=>{}}});
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
   if(u.pathname==='/api/dogs/nearby')return r.fulfill({json:[]});
   if(u.pathname==='/Map/GetBestBin')return r.fulfill({json:data.bins[0]});
   if(u.pathname==='/api/waterpoints')return r.fulfill({json:data.water});
   if(/^\/api\/waterpoints\/\d+\/route$/.test(u.pathname))return r.fulfill({json:{points:[[46.0569,14.5058],[46.058,14.508],[46.060,14.512]],distanceMeters:510,durationSeconds:400}});
   if(u.pathname.startsWith('/api/waterpoints/'))return r.fulfill({json:data.water.find(p=>p.id===Number(u.pathname.split('/').pop()))});
   if(u.pathname==='/api/walking-route')return r.fulfill({json:{points:[[46.0569,14.5058],[46.058,14.508],[46.060,14.512]],distanceMeters:510,durationSeconds:400}});
   if(u.pathname.startsWith('/api/'))return r.fulfill({json:{hotspots:[],events:[],items:[],activeWalkers:0,popularParks:0,trendingRoutes:0}});
   const f=path.resolve(web,'.'+decodeURIComponent(u.pathname));
   if(f.startsWith(web+path.sep)&&fs.existsSync(f)&&fs.statSync(f).isFile())return r.fulfill({path:f});
  }
  return r.abort();
 });
 const start=performance.now();await page.goto('http://127.0.0.1/'+query);
 if(!adminScene)await page.waitForFunction(()=>typeof homeLocations!=='undefined'&&homeLocations&&binLayer&&managedPlaceLayer&&waterLayer);
 await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
 await page.waitForFunction(()=>!map._animatingZoom&&!map._panAnim?._inProgress);
 return {page,errors,initialMs:performance.now()-start,data};
}

const settle=page=>page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
async function inspect(page) {
 return page.evaluate(()=>{
  const box=e=>e.getBoundingClientRect().toJSON(),panel=document.querySelector('.navigation-card'),credit=document.querySelector('.navigation-card__attribution'),attr=document.querySelector('#map .leaflet-control-attribution');
  const m=box(map.getContainer()),p=box(panel),a=box(attr),r=box(credit),bottom=box(document.querySelector('.app-bottom-nav'));
  const links=[...attr.querySelectorAll('a'),...credit.querySelectorAll('a')];
  const clickable=links.every(link=>[...link.getClientRects()].some(rect=>{const hit=document.elementFromPoint(rect.x+rect.width/2,rect.y+rect.height/2);return hit===link||link.contains(hit);}));
  return {m,p,a,r,bottom,clickable,provider:map.getContainer().dataset.basemap,credits:attr.textContent,
   overflow:document.documentElement.scrollWidth>innerWidth+1,button:box(panel.querySelector('button:last-child')),
   font:parseFloat(getComputedStyle(credit).fontSize),attrFont:parseFloat(getComputedStyle(attr).fontSize),
   points:navigation.routeLayer.getLatLngs().map(ll=>{const point=map.latLngToContainerPoint(ll);return {x:m.left+point.x,y:m.top+point.y};}),
   markers:[box(navigation.destinationMarker.getElement()),box(userMarker.getElement())]};
 });
}
function verify(s,label) {
 const overlap=(a,b)=>a.left<b.right&&a.right>b.left&&a.top<b.bottom&&a.bottom>b.top;
 assert.equal(s.overflow,false,label+' horizontal overflow');
 assert.equal(overlap(s.a,s.p),false,label+' attribution/card overlap');
 assert.ok(s.a.top>=s.m.top&&s.a.bottom<=s.m.bottom+1&&s.a.left>=s.m.left&&s.a.right<=s.m.right+1,label+' attribution within map '+JSON.stringify(s));
 assert.ok(Math.abs(s.p.top-s.a.bottom-8)<=1,label+' attribution follows card height');
 if(s.bottom.height)assert.equal(overlap(s.bottom,s.p),false,label+' bottom navigation');
 assert.ok(s.r.top>=s.p.top&&s.r.bottom<=s.p.bottom&&s.r.bottom<=s.m.bottom+1,label+' route credits visible');
 assert.ok(s.clickable,label+' clickable credits');assert.ok(s.font>=12&&s.attrFont>=11,label+' readable credits');
 assert.ok(s.button.height>=44,label+' action touch target');
 for(const point of s.points)assert.ok(point.x>=s.m.left+20&&point.x<=s.m.right-20&&point.y>=s.m.top+40&&point.y<s.a.top-8,label+' route clear '+JSON.stringify(s));
 for(const marker of s.markers)assert.ok(marker.top>=s.m.top&&marker.bottom<s.a.top&&marker.left>=s.m.left&&marker.right<=s.m.right,label+' marker clear '+JSON.stringify(s));
 assert.ok(s.credits.includes('OpenStreetMap'));assert.equal(s.credits.includes('CARTO'),s.provider==='carto');
}
(async()=>{
 const browser=await chromium.launch({channel:process.env.BROWSER_CHANNEL||'msedge',headless:true});const results=[];
 try{
 for(const width of [320,375,390,430,768,1024])for(const provider of ['carto','fallback'])for(const type of ['bin','water','place']){
  const height=width===320?740:width===375?812:width===390?844:width===430?932:900;
  const {page,errors}=await load(browser,{width,height,provider:'carto',tileFailure:provider==='fallback'});
  // Assert final camera geometry deterministically; provider calls and map fitting remain real.
  await page.evaluate(()=>{const fit=map.fitBounds;map.fitBounds=function(bounds,options){return fit.call(this,bounds,{...options,animate:false});};});
  let requests=0;page.on('request',request=>{if(/\/api\/(walking-route|waterpoints\/\d+\/route)$/.test(new URL(request.url()).pathname))requests++;});
  for(const textScale of [1,1.25])for(const long of [false,true]){
   await page.evaluate(async({type,textScale,long})=>{
    stopInAppNavigation();document.documentElement.style.fontSize=(16*textScale)+'px';
    // Model a 34px iPhone bottom inset without changing production styles.
    document.documentElement.style.setProperty('--app-bottom-nav-clearance','122px');
    const bottom=document.querySelector('.app-bottom-nav');bottom.style.bottom='34px';
    userLocation={lat:46.0569,lng:14.5058};
    if(!userMarker)userMarker=L.marker([userLocation.lat,userLocation.lng],{icon:userIcon,pane:'userMarkers'}).addTo(map);
    const destination={id:1,name:long?'Pitnik ob sprehajalni poti pri mestnem parku in otro\u0161kem igri\u0161\u010du':'Mestni park',latitude:46.060,longitude:14.512,categoryKey:'dog-park',categoryLabel:'Pasji park',iconClass:'dd-place-icon--dog-park',...(type==='water'?{waterPointId:1}:{})};
    await startInAppBinNavigation(1,destination,type);
   },{type,textScale,long});
   await page.waitForFunction(()=>navigation.routeStatus==='routed'&&!map._animatingZoom&&!map._panAnim?._inProgress);
   await settle(page);
   await page.waitForFunction(()=>{
    const rect=userMarker.getElement().getBoundingClientRect(),mapRect=map.getContainer().getBoundingClientRect(),point=map.latLngToContainerPoint(userMarker.getLatLng());
    return Math.abs(rect.left+rect.width/2-mapRect.left-point.x)<1&&Math.abs(rect.top+rect.height/2-mapRect.top-point.y)<1;
   });
   const label=JSON.stringify({width,height,provider,type,textScale,long});
   try{const state=await inspect(page);assert.equal(state.provider,provider==='fallback'?'osm':'carto');verify(state,label);results.push({width,height,provider,type,textScale,long});}
   catch(error){await page.screenshot({path:path.join(out,'failure.png')});throw error;}
   const credit=page.locator('.navigation-card__attribution a').first();await credit.focus();
   assert.ok(await credit.evaluate(el=>el===document.activeElement&&el.matches(':focus-visible')&&parseFloat(getComputedStyle(el).outlineWidth)>=2));await credit.evaluate(el=>el.blur());
   if(width===390&&provider==='carto'&&type==='water'&&long)await page.screenshot({path:path.join(out,'navigation-390-'+textScale+'.png')});
  }
  // Height-only changes and visual viewport resizing do not request a new route.
  const requestsBeforeResize=requests;
  await page.locator('.navigation-card__title').evaluate(el=>el.textContent='Kratek cilj');await settle(page);
  const dynamic=await inspect(page);assert.ok(Math.abs(dynamic.p.top-dynamic.a.bottom-8)<=1,'ResizeObserver follows content');
  await page.locator('.navigation-card__title').evaluate(el=>el.textContent='Daljši cilj v mestnem parku ob poti za sprehode');await settle(page);
  const taller=await inspect(page);assert.ok(Math.abs(taller.p.top-taller.a.bottom-8)<=1,'ResizeObserver follows growing content');
  await page.setViewportSize({width,height:height+80});await settle(page);
  const resized=await inspect(page);assert.ok(Math.abs(resized.p.top-resized.a.bottom-8)<=1,'viewport resize follows card');
  assert.ok(resized.a.top>=resized.m.top&&resized.a.bottom<=resized.m.bottom);
  assert.equal(requests,requestsBeforeResize,'layout changes must not call routing');
  await page.evaluate(()=>stopInAppNavigation());await settle(page);
  assert.equal(await page.locator('#map').evaluate(el=>el.style.getPropertyValue('--navigation-attribution-bottom')),'');
  assert.deepEqual(errors,[]);await page.close();
 }
 fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({layouts:results.length,dynamicAndReset:36,results},null,2));console.log(JSON.stringify({layouts:results.length,dynamicAndReset:36}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
