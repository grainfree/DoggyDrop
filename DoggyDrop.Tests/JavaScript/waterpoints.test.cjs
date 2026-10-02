const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),test=require('node:test');
const {loadPopup}=require('./helpers/place-popup-dom.cjs');
const source=fs.readFileSync(path.join(__dirname,'../../DoggyDrop/wwwroot/js/waterpoints.js'),'utf8');
function fixture(send=()=>({ok:true,json:async()=>[]})){const {document}=loadPopup(),window={},calls=[];
 vm.runInNewContext(source,{window,document,URL,L:{divIcon:o=>o},fetch:async(...a)=>{calls.push(a);return send(...a);}});return {api:window.DoggyDropWaterPoints,calls};}
const point={id:1,name:'',latitude:46,longitude:15,access:0,seasonality:0,dogAccess:0};
test('water marker has trusted drop artwork, fixed anchor and no legacy W/DogBeach art',()=>{const {api}=fixture(),icon=api.icon();assert.match(icon.html,/<svg/);assert.equal(icon.className,'waterpoint-marker');assert.equal(icon.iconAnchor[1],38);assert.equal(api.label(point),'Pitnik');});
test('hostile source text and URL render literally with no executable link',()=>{const {api}=fixture();const node=api.popup({...point,name:'</h3><script>alert(1)</script>',sourceName:'<img onerror="evil">',sourceUrl:'javascript:alert(1)'},()=>{});assert.equal(node.querySelectorAll('script').length,0);assert.equal(node.querySelectorAll('img').length,0);assert.equal(node.querySelectorAll('a').length,0);assert.match(node.outerHTML,/&lt;script&gt;/);});
test('water popup provenance link and actions are keyboard native and route by ID',()=>{const {api}=fixture();let selected;const node=api.popup({...point,id:17,sourceName:'OSM',sourceUrl:'https://openstreetmap.org'},id=>selected=id);const button=node.querySelector('button');assert.equal(button.type,'button');button.events.click();assert.equal(selected,17);assert.equal(node.querySelector('a').href,'https://openstreetmap.org/');});
test('unknown evidence never becomes year-round or dog designation',()=>{const {api}=fixture(),text=api.popup(point,()=>{}).textContent;assert.match(text,/Sezonskost ni znana/);assert.match(text,/Dostop ni posebej naveden/);assert.doesNotMatch(text,/za pse|celoletno/);assert.match(api.popup({...point,dogAccess:1},()=>{}).textContent,/posoda za pse ni potrjena/);});
test('nearest uses data, deterministic tie breaks, validates and bounds the useful context',()=>{const {api}=fixture();assert.equal(api.nearest([{...point,id:4},{...point,id:2},{...point,id:3,latitude:90}],{lat:46,lng:15}).point.id,2);assert.equal(api.nearest([point],{lat:0,lng:0}),null);assert.equal(api.nearest([],{lat:46,lng:15}),null);assert.equal(api.nearest([point],{lat:NaN,lng:15}),null);});
test('nearest never calls router per candidate',()=>{const {api,calls}=fixture();api.nearest(Array.from({length:3000},(_,i)=>({...point,id:i+1})),{lat:46,lng:15});assert.equal(calls.length,0);});
test('water route uses same-origin CSRF with ID and origin only, never cluster centroid',async()=>{const {api,calls}=fixture(()=>({ok:true,json:async()=>({points:[[46,15],[46.1,15.1]],distanceMeters:100,durationSeconds:90})}));const r=await api.route({lat:46,lng:15},18,'csrf');assert.equal(r.durationSeconds,90);assert.equal(calls[0][0],'/api/waterpoints/18/route');const o=calls[0][1];assert.equal(o.headers.RequestVerificationToken,'csrf');assert.equal(o.cache,'no-store');assert.deepEqual(JSON.parse(o.body),{origin:{latitude:46,longitude:15}});});
test('retired route is terminal and cannot fall back to approximate guidance',async()=>{const {api}=fixture(()=>({status:410,ok:false}));await assert.rejects(()=>api.route({lat:46,lng:15},1,'token'),e=>e.unavailable===true);});
for(const status of [429,503])test(`water provider ${status} gives no invented ETA and no automatic retry`,async()=>{const {api,calls}=fixture(()=>({status,ok:false}));assert.equal(await api.route({lat:46,lng:15},1,'t'),null);assert.equal(calls.length,1);});
test('cancelled water routing propagates cancellation',async()=>{const {api}=fixture(()=>{const e=Error();e.name='AbortError';throw e;});await assert.rejects(()=>api.route({lat:46,lng:15},1,'t'),{name:'AbortError'});});
test('Home water integration isolates infrastructure and revalidates before new guidance',()=>{const home=fs.readFileSync(path.join(__dirname,'../../DoggyDrop/Views/Map/Index.cshtml'),'utf8');assert.match(home,/waterLayer = homeLocations.createLayer/);assert.match(home,/wireLayerToggle\("showDrinkingWater", waterLayer\)/);assert.match(home,/DoggyDropWaterPoints.current\(id\)/);assert.match(home,/waterPointId:point.id/);assert.match(home,/sequence !== navigation.waterLookupSequence/);assert.match(home,/error.unavailable.*stopInAppNavigation/);});
const home=fs.readFileSync(path.join(__dirname,'../../DoggyDrop/Views/Map/Index.cshtml'),'utf8');
function waterLifecycle(){
 const requests=[],started=[],alerts=[];
 const context={navigation:{waterLookupSequence:0},userLocation:{lat:46,lng:15},navigator:{},window:{isSecureContext:true},
  DoggyDropWaterPoints:{current:id=>new Promise((resolve,reject)=>requests.push({id,resolve,reject})),valid:fixture().api.valid,label:fixture().api.label},
  navigateToPlaceInApp:(...args)=>started.push(args),alert:m=>alerts.push(m)};
 context.setHomeUserLocation=location=>{context.userLocation=location;};
 vm.createContext(context);vm.runInContext(home.slice(home.indexOf('        function waterOrigin()'),home.indexOf('        function navigateToManagedPlaceInApp(')),context);
 return {context,requests,started,alerts};
}
test('newer water selection wins against a late successful lookup',async()=>{const f=waterLifecycle();const first=f.context.navigateToWater(1);await Promise.resolve();const second=f.context.navigateToWater(2);await Promise.resolve();f.requests[1].resolve({...point,id:2});await second;f.requests[0].resolve(point);await first;assert.equal(f.started.length,1);assert.equal(f.started[0][4].waterPointId,2);});
test('cancelled water selection cannot restart navigation after lookup',async()=>{const f=waterLifecycle();const pending=f.context.navigateToWater(1);await Promise.resolve();f.context.navigation.waterLookupSequence++;f.requests[0].resolve(point);await pending;assert.equal(f.started.length,0);});
test('late unavailable lookup cannot alert over a newer successful water target',async()=>{const f=waterLifecycle();const first=f.context.navigateToWater(1);await Promise.resolve();const second=f.context.navigateToWater(2);await Promise.resolve();f.requests[1].resolve({...point,id:2});await second;f.requests[0].reject(Error('retired'));await first;assert.equal(f.started.length,1);assert.deepEqual(f.alerts,[]);});
test('current unavailable water target never begins navigation',async()=>{const f=waterLifecycle();const pending=f.context.navigateToWater(1);await Promise.resolve();f.requests[0].reject(Error('retired'));await pending;assert.equal(f.started.length,0);assert.deepEqual(f.alerts,['retired']);});
test('Home location helper creates one existing-style user marker directly on map and reuses it',()=>{
 const map={},icon={},created=[];
 const context={userLocation:null,userMarker:null,map,userIcon:icon,L:{marker:(position,options)=>{
  const marker={position,options,addTo:target=>{assert.equal(target,map);return marker;},bindPopup:()=>marker,setLatLng:value=>{marker.position=value;}};
  created.push(marker);return marker;
 }}};
 vm.createContext(context);vm.runInContext(home.slice(home.indexOf('        function setHomeUserLocation('),home.indexOf('        function waterOrigin(')),context);
 context.setHomeUserLocation({lat:46,lng:15});const original=context.userMarker;
 context.setHomeUserLocation({lat:46.01,lng:15.01});
 assert.equal(created.length,1);assert.equal(context.userMarker,original);assert.equal(original.options.icon,icon);assert.equal(original.options.pane,'userMarkers');
 assert.deepEqual(Array.from(original.position),[46.01,15.01]);
});
