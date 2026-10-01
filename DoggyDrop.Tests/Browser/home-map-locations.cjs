// Actual captured Home Razor/JS + real Leaflet/plugin. All requests fulfilled offline or aborted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const web=path.resolve(__dirname,'../../DoggyDrop/wwwroot');
const capture=process.env.DOGGYDROP_POPUP_CAPTURE,assets=process.env.LEAFLET_TEST_ASSETS,out=process.env.HOME_CLUSTER_RESULTS;
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
async function load(browser,{count=250,width=390,dpr=1,provider='osm',query='',active=false,tileFailure=false}={}){
 const page=await browser.newPage({viewport:{width,height:900},deviceScaleFactor:dpr}),errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 const data=dataset(count);
 let html=fs.readFileSync(capture+'/'+(active?'home-active':provider==='carto'?'home-carto':'home-anonymous')+'.html','utf8');
 html=html.replace(/const bins = [^\r\n]+;/,'const bins = '+JSON.stringify(data.bins)+';')
   .replace(/const managedPlaces = \([^\r\n]+\)\.filter/,'const managedPlaces = ('+JSON.stringify(data.places)+').filter')
   .replace(/<strong>\d+<\/strong>(\s*<span>pasjih košev na zemljevidu)/,'<strong>'+data.bins.length+'</strong>$1');
 await page.addInitScript(()=>{
  localStorage.setItem('doggydrop.homeIntroDismissed.v1','true');
  Object.defineProperty(navigator,'geolocation',{value:{getCurrentPosition:()=>{},watchPosition:()=>1,clearWatch:()=>{}}});
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
   if(u.pathname.startsWith('/api/'))return r.fulfill({json:{hotspots:[],events:[],items:[],activeWalkers:0,popularParks:0,trendingRoutes:0}});
   const f=path.resolve(web,'.'+decodeURIComponent(u.pathname));
   if(f.startsWith(web+path.sep)&&fs.existsSync(f)&&fs.statSync(f).isFile())return r.fulfill({path:f});
  }
  return r.abort();
 });
 const start=performance.now();await page.goto('http://127.0.0.1/'+query);
 await page.waitForFunction(()=>typeof homeLocations!=='undefined'&&homeLocations&&binLayer&&managedPlaceLayer);
 await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
 return {page,errors,initialMs:performance.now()-start,data};
}
async function counts(page){return page.evaluate(()=>{
 const groups=[];map.eachLayer(l=>{if(l instanceof L.MarkerClusterGroup)groups.push(l);});
 const group=groups[0],logical=[...(map.hasLayer(binLayer)?binLayer.getLayers():[]),...(map.hasLayer(managedPlaceLayer)?managedPlaceLayer.getLayers():[])];
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
const scenes=[['national',[46.15,14.9],8],['regional',[46.0569,14.5058],11],['city',[46.0569,14.5058],14],['dense-city',[46.0569,14.5058],16],['street',[46.0569,14.5058],18],['place-bins',[46.0569,14.5058],17],['logos',[46.0569,14.5058],13]];
(async()=>{
 const browser=await chromium.launch({channel:process.env.BROWSER_CHANNEL||'msedge',headless:true});
 const layouts=[],performanceResults=[],lifecycle=[],cutoffs=[];
 try{
  for(const width of (process.env.CLUSTER_LIFECYCLE_ONLY?[]:[320,375,390,430,768,1024,1440]))for(const dpr of [1,2])for(const provider of ['osm','carto']){
   const {page,errors}=await load(browser,{width,dpr,provider});
   for(const [scene,center,zoom] of scenes){
    await page.evaluate(({center,zoom})=>map.setView(center,zoom,{animate:false}),{center,zoom});
    const c=await counts(page);conserve(c);assert.equal(c.overflow,false,JSON.stringify({width,scene,c}));
    if(zoom<=11){assert.ok(c.clusters>0);assert.ok(c.dom<150,'broad zoom still crowded');}
    layouts.push({width,dpr,provider,scene,zoom,...c});
    if(dpr===1&&provider==='carto'&&[390,1440].includes(width)&&['national','city','street'].includes(scene)) {
     await page.screenshot({path:path.join(out,`${scene}-${width}.png`)});
     if(scene==='national') {
      await page.evaluate(()=>{
       window.beforeLayer=L.layerGroup([...binLayer.getLayers(),...managedPlaceLayer.getLayers()]);
       binLayer.remove();managedPlaceLayer.remove();beforeLayer.addTo(map);
       map.getContainer().classList.remove('home-map--broad','home-map--mid');
      });
      await page.screenshot({path:path.join(out,`before-national-${width}.png`)});
      await page.evaluate(()=>{beforeLayer.remove();binLayer.addTo(map);managedPlaceLayer.addTo(map);map.fire('zoomend');});
      conserve(await counts(page));
     }
    }
   }
   assert.deepEqual(errors,[]);await page.close();
  }
  for(const count of (process.env.CLUSTER_LIFECYCLE_ONLY?[]:[250,500,1500,3000])){
   const {page,errors,initialMs}=await load(browser,{count,width:1440});
   const times=await page.evaluate(()=>{
    const time=fn=>{const t=performance.now();fn();return performance.now()-t;};
    const pan=time(()=>map.panTo([46.1,14.7],{animate:false}));
    const zoom=time(()=>map.setView([46.0569,14.5058],14,{animate:false}));
    const filter=time(()=>{binLayer.remove();binLayer.addTo(map);managedPlaceLayer.remove();managedPlaceLayer.addTo(map);});
    const members=binLayer.getLayers();const rebuild=time(()=>{binLayer.setLayers([]);binLayer.setLayers(members);});
    map.fitBounds(L.latLngBounds([...bins,...managedPlaces].map(p=>[p.latitude,p.longitude])),{animate:false});
    return {panMs:pan,zoomMs:zoom,filterMs:filter,rebuildMs:rebuild};
   });
   const c=await counts(page);conserve(c);assert.equal(c.count,count);assert.ok(c.dom<250,JSON.stringify(c));
   performanceResults.push({count,initialMs,...times,...c});
   assert.deepEqual(errors,[]);await page.close();
  }
  const {page,errors}=await load(browser,{width:390});
  // Compare broad-cluster cutoffs using the same real plugin and fixed city scene.
  for(const cutoff of [16,17,18])cutoffs.push(await page.evaluate(cutoff=>{
   const group=L.markerClusterGroup({maxClusterRadius:z=>z<14?64:z<cutoff?48:22,animate:false}).addTo(map);
   const sample=Array.from({length:80},(_,i)=>L.marker([46.0569+(i%10)*.0002,14.5058+Math.floor(i/10)*.0003]));
   group.addLayers(sample);const zooms=[];
   for(const zoom of [14,16,17,18,20]){map.setView([46.0576,14.5068],zoom,{animate:false});const parents=new Set(sample.map(m=>group.getVisibleParent(m)).filter(Boolean));zooms.push({zoom,representatives:parents.size,individuals:sample.filter(m=>map.hasLayer(m)).length});}
   map.removeLayer(group);return {cutoff,zooms};
  },cutoff));
  // Filters, repeated toggles, category replacement and full restoration.
  for(const id of ['showBins','showPlaces']){
   await page.evaluate(id=>{const e=document.getElementById(id);e.checked=false;e.dispatchEvent(new Event('change'));},id);conserve(await counts(page));
   await page.evaluate(id=>{const e=document.getElementById(id);e.checked=true;e.dispatchEvent(new Event('change'));},id);conserve(await counts(page));lifecycle.push(id+' off/on');
  }
  await page.evaluate(()=>{
   const all=managedPlaceLayer.getLayers();window.testAllPlaces=all;
   managedPlaceLayer.setLayers(all.filter(m=>managedPlaces.find(p=>p.id===m.options.placeId).categoryKey==='dog-park'));
  });conserve(await counts(page));
  await page.evaluate(()=>{managedPlaceLayer.setLayers(testAllPlaces);managedPlaceLayer.setLayers(testAllPlaces);});conserve(await counts(page));lifecycle.push('category replacement/restore');
  // Logo/Featured focuses from a broad cluster and remains visible when zoomed back out.
  await page.evaluate(()=>{map.setView([46.0569,14.5058],8,{animate:false});focusExploreItem('place-1');});
  await page.waitForFunction(()=>placeMarkers.get('place-1').isPopupOpen());
  await page.waitForFunction(()=>placeMarkers.get('place-1').getElement()?.querySelector('.has-image'));
  assert.ok(await page.evaluate(()=>!!placeMarkers.get('place-1').getElement().querySelector('.managed-place-pin--featured.managed-place-pin--selected')));
  await page.evaluate(()=>map.setZoom(8,{animate:false}));assert.ok(await page.evaluate(()=>map.hasLayer(placeMarkers.get('place-1'))));conserve(await counts(page));lifecycle.push('selected logo/Featured persistent visibility');
  await page.evaluate(()=>{map.closePopup();focusExploreItem('place-6');});
  await page.waitForFunction(()=>placeMarkers.get('place-6').isPopupOpen(),null,{timeout:5000}).catch(async e=>{
   console.log('Focus diagnostic',errors,await page.evaluate(()=>{const m=placeMarkers.get('place-6');return {zoom:map.getZoom(),icon:!!m.getElement(),onMap:map.hasLayer(m),parent:!!m.__parent,parentZoom:m.__parent?._zoom,visible:map.hasLayer(managedPlaceLayer),popup:!!m.isPopupOpen()};}));throw e;
  });
  await page.waitForFunction(()=>placeMarkers.get('place-6').getElement()?.querySelector('img')?.hidden);
  assert.ok(await page.evaluate(()=>!placeMarkers.get('place-6').getElement().querySelector('.has-image')));lifecycle.push('broken-logo category fallback');
  await page.evaluate(()=>{map.closePopup();map.setZoom(8,{animate:false});focusExploreItem('bin-2');});
  await page.waitForFunction(()=>activeBinId===2&&map.hasLayer(binMarkers.get(2)));conserve(await counts(page));lifecycle.push('selected bin detail');
  const selection=await page.evaluate(()=>{
   map.setZoom(8,{animate:false});const marker=binMarkers.get(2),pane=map.getPane('focusedLocations');
   return {selected:marker.getElement().parentElement===pane,aboveClusters:Number(pane.style.zIndex)>Number(map.getPane('placeMarkers').style.zIndex),belowUser:Number(pane.style.zIndex)<Number(map.getPane('userMarkers').style.zIndex)};
  });assert.deepEqual(selection,{selected:true,aboveClusters:true,belowUser:true});lifecycle.push('selected bin above clusters, below user');
  for(const enabled of [true,false]){
   await page.evaluate(enabled=>{
    closeBinDetail();if(enabled)binLayer.addTo(map);else binLayer.remove();
    navigator.geolocation.getCurrentPosition=cb=>cb({coords:{latitude:46.055,longitude:14.50}});
    findNearestTrashBin();
   },enabled);
   await page.waitForFunction(()=>nearestBinPreview?.id===2);
   await page.evaluate(()=>closeNearestBinPreview());
  }
  await page.evaluate(()=>binLayer.addTo(map));lifecycle.push('nearest response invariant with locations shown/hidden');
  // Actual routes always receive the original data coordinate; no GPS/provider requests run.
  const route=await page.evaluate(async()=>{
   closeBinDetail();userLocation={lat:46.055,lng:14.50};requestNavigationRoute=()=>{};
   const bin=bins[0];await startInAppBinNavigation(bin.id);
   const target=navigation.destinationMarker.getLatLng(),hidden=!map.hasLayer(binLayer)&&!map.hasLayer(managedPlaceLayer);
   stopInAppNavigation();return {target:[target.lat,target.lng],expected:[bin.latitude,bin.longitude],hidden};
  });assert.deepEqual(route.target,route.expected);assert.ok(route.hidden);conserve(await counts(page));lifecycle.push('navigation suspend/restore exact target');
  const placeRoute=await page.evaluate(async()=>{
   const place=managedPlaces[0];await startInAppBinNavigation(0,place,'place');
   const target=navigation.destinationMarker.getLatLng();stopInAppNavigation();
   return {target:[target.lat,target.lng],expected:[place.latitude,place.longitude]};
  });assert.deepEqual(placeRoute.target,placeRoute.expected);conserve(await counts(page));lifecycle.push('Place directions actual coordinate');
  // Same-coordinate spiderfy, keyboard Enter and Space.
  await page.evaluate(()=>{map.closePopup();map.setView([46.0569,14.5058],20,{animate:false});window.spiderEvents=0;map.eachLayer(l=>{if(l instanceof L.MarkerClusterGroup)l.on('spiderfied',()=>spiderEvents++);});});
  let cluster=page.locator('.home-location-cluster').first();await cluster.focus();await page.keyboard.press('Enter');
  await page.waitForFunction(()=>spiderEvents===1);lifecycle.push('coincident spiderfy Enter');
  await page.evaluate(()=>{map.setZoom(19,{animate:false});map.setZoom(20,{animate:false});});
  cluster=page.locator('.home-location-cluster').first();await cluster.focus();await page.keyboard.press('Space');
  await page.waitForFunction(()=>spiderEvents===2);lifecycle.push('coincident spiderfy Space');
  assert.deepEqual(errors,[]);await page.close();
  for(const query of ['?placeId=1','?binId=2']){
   const {page,errors}=await load(browser,{query});
   await page.waitForFunction(query=>query.includes('placeId')?placeMarkers.get('place-1').isPopupOpen():activeBinId===2,query);
   if(query.includes('placeId')){
    const same=await page.evaluate(()=>{
     const center=map.getCenter();navigator.geolocation.getCurrentPosition=cb=>cb({coords:{latitude:46.55,longitude:15.64}});
     focusMapNearUser(true);return map.getCenter().equals(center)&&userMarker.getLatLng().lat===46.55;
    });assert.ok(same,'deep-linked camera must survive geolocation while updating user marker');
   }
   conserve(await counts(page));assert.deepEqual(errors,[]);lifecycle.push('deep link '+query);await page.close();
  }
  for(const active of [false,true]){
   const {page,errors}=await load(browser,{active,provider:'carto',tileFailure:!active});
   if(!active)await page.waitForFunction(()=>map.getContainer().dataset.basemap==='osm');
   conserve(await counts(page));
   assert.ok(await page.evaluate(()=>map.getContainer().querySelector('.leaflet-control-attribution').textContent.includes('OpenStreetMap')));
   if(active)assert.ok(await page.evaluate(()=>map.hasLayer(homeWalkTrailLayer)&&!binLayer.getLayers().includes(homeWalkTrailLayer)));
   assert.deepEqual(errors,[]);lifecycle.push(active?'Home active trail outside clusters':'CARTO 429 to OSM fallback');await page.close();
  }
  const result={layouts:layouts.length,performanceCases:performanceResults.length,lifecycleCases:lifecycle.length,layoutResults:layouts,performanceResults,lifecycle,cutoffs};
  fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(result,null,2));
  console.log(JSON.stringify({layouts:layouts.length,performanceResults,lifecycle,cutoffs},null,2));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
