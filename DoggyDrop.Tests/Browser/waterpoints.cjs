// Actual captured Home Razor/JS + real Leaflet/plugin. All requests fulfilled offline or aborted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const web=path.resolve(__dirname,'../../DoggyDrop/wwwroot');
const capture=process.env.DOGGYDROP_POPUP_CAPTURE,assets=process.env.LEAFLET_TEST_ASSETS,out=process.env.WATER_RESULTS;
if(!capture||!assets||!out)throw Error('Set DOGGYDROP_POPUP_CAPTURE, LEAFLET_TEST_ASSETS, HOME_CLUSTER_RESULTS; see docs/home-map-clustering.md');
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
async function load(browser,{count=250,width=390,dpr=1,provider='osm',query='',active=false,tileFailure=false,waterCount=500,adminScene=null}={}){
 const page=await browser.newPage({viewport:{width,height:900},deviceScaleFactor:dpr}),errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 const data=dataset(count);
 data.water=Array.from({length:waterCount},(_,i)=>({id:i+1,name:i===0?'<script>hostile</script>':'Pitnik '+i,latitude:46.0569+(i%50)*.0001,longitude:14.5058+Math.floor(i/50)*.0001,seasonality:0,access:0,dogAccess:0,sourceName:'OSM',sourceUrl:'https://openstreetmap.org'}));
 let html=fs.readFileSync(capture+'/'+(adminScene|| (active?'home-active':provider==='carto'?'home-carto':'home-anonymous'))+'.html','utf8');
 html=html.replace(/let waterPoints = [^\r\n]+;/,'let waterPoints = '+JSON.stringify(data.water).replaceAll('<','\\u003c')+';').replace(/const bins = [^\r\n]+;/,'const bins = '+JSON.stringify(data.bins)+';')
   .replace(/const managedPlaces = \([^\r\n]+\)\.filter/,'const managedPlaces = ('+JSON.stringify(data.places)+').filter')
   .replace(/<strong>\d+<\/strong>(\s*<span>pasjih košev na zemljevidu)/,'<strong>'+data.bins.length+'</strong>$1');
 await page.addInitScript(()=>{
  localStorage.setItem('doggydrop.homeIntroDismissed.v1','true');
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
   if(/^\/api\/waterpoints\/\d+\/route$/.test(u.pathname))return r.fulfill({json:{points:[[46.0569,14.5058],[46.057,14.506]],distanceMeters:100,durationSeconds:80}});
   if(u.pathname.startsWith('/api/waterpoints/'))return r.fulfill({json:data.water.find(p=>p.id===Number(u.pathname.split('/').pop()))});
   if(u.pathname.startsWith('/api/'))return r.fulfill({json:{hotspots:[],events:[],items:[],activeWalkers:0,popularParks:0,trendingRoutes:0}});
   const f=path.resolve(web,'.'+decodeURIComponent(u.pathname));
   if(f.startsWith(web+path.sep)&&fs.existsSync(f)&&fs.statSync(f).isFile())return r.fulfill({path:f});
  }
  return r.abort();
 });
 const start=performance.now();await page.goto('http://127.0.0.1/'+query);
 if(!adminScene)await page.waitForFunction(()=>typeof homeLocations!=='undefined'&&homeLocations&&binLayer&&managedPlaceLayer&&waterLayer);
 await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
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

(async()=>{
 const browser=await chromium.launch({channel:process.env.BROWSER_CHANNEL||'msedge',headless:true});const layouts=[],performanceResults=[],filters=[],userMarkerLifecycle=[];
 try{
  for(const width of [320,375,390,430,768,1024,1440])for(const provider of ['carto','osm'])for(const dpr of [1,2]){
   const {page,errors}=await load(browser,{width,provider,dpr,tileFailure:provider==='osm'});
   for(const [scene,zoom] of [['national',8],['dense-city',16],['popup',18]]){
    await page.evaluate(({zoom,popup})=>{map.setView([46.057,14.506],zoom,{animate:false});if(popup)homeLocations.reveal(waterMarkers.get(1),()=>waterMarkers.get(1).openPopup());},{zoom,popup:scene==='popup'});
    const c=await counts(page);conserve(c);assert.equal(c.overflow,false,JSON.stringify({width,provider,dpr,scene,c}));
    if(scene==='popup'){await page.waitForFunction(()=>[...document.querySelectorAll('.leaflet-popup')].some(p=>getComputedStyle(p).opacity==='1'));if(width===390&&dpr===1&&provider==='carto')await page.screenshot({path:path.join(out,'water-actual-popup-390.png')});const box=await page.locator('.waterpoint-popup').boundingBox();assert.ok(box&&box.width<=width);assert.equal(await page.locator('.waterpoint-popup script').count(),0);}
    layouts.push({width,provider,dpr,scene,...c});
   }
   await page.evaluate(()=>findNearestWater());await page.waitForFunction(()=>document.getElementById('waterStatus').textContent.includes('zračni'));
   conserve(await counts(page));layouts.push({width,provider,dpr,scene:'nearest'});
   await page.locator('.waterpoint-popup button').last().click();await page.waitForFunction(()=>navigation.routeStatus==='routed');
   assert.equal(await page.evaluate(()=>navigation.destination.waterPointId),1);assert.equal(await page.evaluate(()=>navigation.durationSeconds),80);
   assert.equal((await counts(page)).overflow,false);layouts.push({width,provider,dpr,scene:'directions'});
   await page.evaluate(()=>stopInAppNavigation());conserve(await counts(page));
   if(width===390&&dpr===1&&provider==='carto')await page.screenshot({path:path.join(out,'water-popup-390.png')});
   assert.deepEqual(errors,[]);await page.close();
  }
  for(const entry of ['direct-water','nearest-water','bin','place']){
   const {page,errors}=await load(browser,{query:'?binId=2'});
   assert.equal(await page.evaluate(()=>userMarker==null&&userLocation==null),true,'bin deep link starts without user marker');
   await page.evaluate(()=>{navigator.geolocation.watchPosition=callback=>{window.markerTestGps=callback;return 77;};});
   if(entry==='nearest-water'){
    await page.evaluate(()=>findNearestWater());
    assert.equal(await page.evaluate(()=>!!userMarker&&userMarker.getLatLng().lat===userLocation.lat&&userMarker.getLatLng().lng===userLocation.lng),true);
    await page.evaluate(()=>{window.originalUserMarker=userMarker;});
   }
   await page.evaluate(entry=>{
    if(entry==='bin')navigateToBinInApp(bins[0].id);
    else if(entry==='place')navigateToManagedPlaceInApp(managedPlaces[0].id);
    else return navigateToWater(1);
   },entry);
   await page.waitForFunction(()=>navigation.active&&!navigation.routePending);
   const assertMarker=async(expected)=>{
    const state=await page.evaluate(()=>{
     let group;map.eachLayer(layer=>{if(layer instanceof L.MarkerClusterGroup)group=layer;});
     const markerLayers=[];map.eachLayer(layer=>{if(layer instanceof L.Marker&&layer.options.icon===userIcon)markerLayers.push(layer);});
     return {exists:!!userMarker,position:userMarker?[userMarker.getLatLng().lat,userMarker.getLatLng().lng]:null,
      count:markerLayers.length,onMap:!!userMarker&&map.hasLayer(userMarker),clustered:!!userMarker&&group.hasLayer(userMarker),
      logical:[binLayer,managedPlaceLayer,waterLayer].some(layer=>layer.getLayers().includes(userMarker)),
      same:!window.originalUserMarker||window.originalUserMarker===userMarker};
    });
    assert.equal(state.exists,true);assert.deepEqual(state.position,expected);assert.equal(state.count,1);assert.equal(state.onMap,true);
    assert.equal(state.clustered,false);assert.equal(state.logical,false);assert.equal(state.same,true);
   };
   await assertMarker([46.0569,14.5058]);await page.evaluate(()=>{window.originalUserMarker=userMarker;window.markerTestGps({coords:{latitude:46.057,longitude:14.5059}});});
   await assertMarker([46.057,14.5059]);
   await page.evaluate(async()=>{stopInAppNavigation();userLocation=null;await findNearestWater();await navigateToWater(1);});
   await page.waitForFunction(()=>navigation.active&&!navigation.routePending);await assertMarker([46.0569,14.5058]);
   await page.evaluate(()=>stopInAppNavigation());conserve(await counts(page));assert.deepEqual(errors,[]);
   userMarkerLifecycle.push(entry);await page.close();
  }
  for(const waterCount of [500,1000,1500,2500]){
   const {page,errors,initialMs}=await load(browser,{count:600,waterCount,width:1440});
   for(let state=0;state<8;state++){
    await page.evaluate(state=>{[binLayer,managedPlaceLayer,waterLayer].forEach((l,i)=>{if(state&(1<<i))l.addTo(map);else l.remove();});},state);
    const represented=await page.evaluate(()=>{
     const logical=[binLayer,managedPlaceLayer,waterLayer].filter(l=>map.hasLayer(l)).flatMap(l=>l.getLayers());
     if(logical.length)map.fitBounds(L.latLngBounds(logical.map(m=>m.getLatLng())),{animate:false});
     let group;map.eachLayer(l=>{if(l instanceof L.MarkerClusterGroup)group=l;});
     const parents=new Set(logical.map(m=>group.getVisibleParent(m)||m));
     return [...parents].reduce((sum,p)=>sum+(p.getChildCount?p.getChildCount():1),0);
   });
   const c=await counts(page);conserve(c);assert.equal(represented,c.expected);filters.push({waterCount,state,count:c.count});
   }
   const times=await page.evaluate(()=>{const t=performance.now();for(let i=0;i<4;i++){waterLayer.remove();waterLayer.addTo(map);renderWaterPoints();}return {refreshMs:performance.now()-t};});
   conserve(await counts(page));assert.equal(await page.evaluate(()=>waterLayer.getLayers().length),waterCount);
   await page.evaluate(()=>homeLocations.reveal(waterMarkers.get(1),()=>waterMarkers.get(1).openPopup()));conserve(await counts(page));
   assert.deepEqual(errors,[]);performanceResults.push({waterCount,total:600+waterCount,initialMs,...times});await page.close();
  }
  for(const width of [320,375,390,430,768,1024,1440])for(const adminScene of ['admin-list','admin-create','admin-edit','import-preview']){
   const {page,errors}=await load(browser,{width,adminScene});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false,`${adminScene} ${width}`);
   assert.deepEqual(errors,[]);layouts.push({width,scene:adminScene});if(width===390&&adminScene==='admin-create')await page.screenshot({path:path.join(out,'admin-create-390.png')});await page.close();
  }
  fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({layouts:layouts.length,filters:filters.length,performanceCases:performanceResults.length,userMarkerLifecycle,layoutResults:layouts,filterResults:filters,performanceResults},null,2));
  console.log(JSON.stringify({layouts:layouts.length,filters:filters.length,userMarkerLifecycle,performanceResults}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
