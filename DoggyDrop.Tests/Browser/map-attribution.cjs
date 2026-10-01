const fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
// Run after HTTP tests capture Razor HTML; all network traffic is fulfilled locally or aborted.
const root=path.resolve(__dirname,'../..');
const out=process.env.ATTRIBUTION_RESULTS,old=process.env.LEAFLET_TEST_ASSETS,capture=process.env.DOGGYDROP_POPUP_CAPTURE;
if(!out||!old||!capture)throw new Error('Set ATTRIBUTION_RESULTS, LEAFLET_TEST_ASSETS and DOGGYDROP_POPUP_CAPTURE; see docs/map-basemap.md.');
fs.mkdirSync(out,{recursive:true});
const asset=relative=>path.join(root,'DoggyDrop',relative);
const home=fs.readFileSync(capture+'/home-carto.html','utf8'),edit=fs.readFileSync(capture+'/edit-map.html','utf8');
const source=fs.readFileSync(root+'/DoggyDrop/Views/Map/Index.cshtml','utf8');
const build=source.slice(source.indexOf('        function buildManagedPlaceLayer('),source.indexOf('        function wireLayerToggle('));
const activeSource=fs.readFileSync(root+'/DoggyDrop/Views/Walks/Active.cshtml','utf8');
const activeControls=activeSource.slice(activeSource.indexOf('        <div class="active-walk-map-controls"'),activeSource.indexOf('    <aside class="active-walk-cockpit"')).replace(/<\/article>\s*$/, '');
const cockpit=activeSource.slice(activeSource.indexOf('    <aside class="active-walk-cockpit"'),activeSource.indexOf('        <p id="cockpitGpsNotice"')).replace('@Model.Dog?.Name','Luna').replace('@SlovenianFormatting.WalkDistance(Model.DistanceMeters)','1,2 km')+'</aside>';
const activeCss=[...activeSource.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g)].map(x=>x[1].replaceAll('@@','@')).join('\n');
const clean=html=>html.replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi,'');
const scenes=[['home-city',13],['home-neighborhood',16],['home-places',16],['home-bins',17],['active-route',16],['admin-coordinate',17],['home-navigation',16],['home-walk',16],['home-nearest',16],['walk-details',16],['planner',16],['place-details',16],['admin-create',17],['add-bin',16],['legacy-home',16],['home-osm',16]];
const isolated={
 'walk-details':['Walks/Details','summaryMap','walk-summary__map-card',''],
 'place-details':['Places/Details','placeDetailsMap','places-details','places-details__map places-leaflet-map'],
 'add-bin':['Map/Add','map','container',''],
 'legacy-home':['Home/Index','map','','']
};
const viewCss=view=>[...fs.readFileSync(asset('Views/'+view+'.cshtml'),'utf8').matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g)].map(x=>x[1].replaceAll('@@','@')).join('\n');
const syntheticTile='<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256"><rect width="256" height="256" fill="#edf2e7"/><path d="M0 120H256M120 0V256" stroke="white" stroke-width="12"/></svg>';
function htmlFor(scene){
 if(scene==='home-walk')return clean(fs.readFileSync(capture+'/home-active-carto.html','utf8'));
 if(scene==='admin-coordinate'||scene==='admin-create')return clean(edit); // Create and Edit share _Form and the picker.
 if(scene==='planner')return clean(fs.readFileSync(capture+'/planner-routed.html','utf8'));
 if(isolated[scene]){const [view,id,wrapper,cls]=isolated[scene];return '<!doctype html><html><head>'+clean(home.match(/<head>([\s\S]*?)<\/head>/)[1])+`<link rel="stylesheet" href="/css/places.css"><style>${viewCss(view)}</style></head><body><main class="${wrapper}"><div id="${id}" class="${cls}" style="min-height:520px"></div></main></body></html>`;}

 if(scene==='active-route')return '<!doctype html><html lang="sl"><head>'+home.match(/<head>([\s\S]*?)<\/head>/)[1]+`<style>${activeCss}</style></head><body class="app-shell"><section class="app-page active-walk-page"><header class="active-walk-hero"><span>Aktiven sprehod · testni prikaz</span><h1>Luna</h1></header><article class="active-walk-map-card"><div class="active-walk-map-card__header"><div><h2>GPS pot</h2><p>Testna pot, brez snemanja</p></div><button>Končaj</button></div><div id="walkMap"></div>${activeControls}</article>${cockpit}</section></body></html>`;
 return clean(home);
}
async function setup(page,scene,zoom){
 await page.route('http://127.0.0.1:8198/?scene=*',r=>r.fulfill({contentType:'text/html; charset=utf-8',body:htmlFor(scene)}));
 await page.goto('http://127.0.0.1:8198/?scene='+scene);
 await page.addScriptTag({url:'/leaflet.js'});
 await page.evaluate(()=>{window.fixtureMaps=[];const create=L.map;L.map=function(...args){const map=create(...args);fixtureMaps.push(map);return map;};});
 await page.evaluate(provider=>new Promise((resolve,reject)=>{const script=document.createElement('script');script.src='/js/map-basemap.js';script.dataset.provider=provider;script.dataset.cartoApiKey='fixture-public-key';script.onload=resolve;script.onerror=reject;document.body.append(script);}),scene==='home-osm'?'osm':'carto');
 for(const script of ['place-marker','place-popup','bin-marker'])await page.addScriptTag({url:'/js/'+script+'.js'});
 if(scene==='admin-coordinate'||scene==='admin-create'){
  await page.locator('#Latitude').fill('46.556');await page.locator('#Longitude').fill('15.645');
  await page.addScriptTag({url:'/js/place-editor.js'});await page.locator('#placePickerMap').scrollIntoViewIfNeeded();
 }else await page.evaluate(({scene,zoom,build,isolatedId})=>{
  const active=scene==='active-route',id=active?'walkMap':scene==='planner'?'plannerRouteMap':isolatedId||'map';
  if(scene==='home-navigation')document.body.classList.add('map-bin-navigation-active');
  if(scene==='home-walk')document.body.classList.add('map-walk-active');
  if(scene==='home-nearest')document.body.classList.add('map-nearest-preview-active');
  const center=[46.5638,15.6445];window.fixtureMap=L.map(id).setView(center,zoom);DoggyDropBasemap.addTo(fixtureMap,{maxZoom:active?19:20});
  fixtureMap.createPane('placeMarkers').style.zIndex=610;
  window.placeMarkers=new Map();window.attachManagedPlaceImage=DoggyDropPlaceMarker.attachImage;window.navigateToManagedPlaceInApp=()=>{};
  const count=scene==='home-places'?12:3,places=Array.from({length:count},(_,i)=>({id:i+1,name:i%2?'Pasji park':'Mr.Pet · testna lokacija',categoryLabel:i%2?'Pasji park':'Trgovina',categoryKey:i%2?'dog-park':'pet-shop',iconClass:i%2?'dd-place-icon--dog-park':'dd-place-icon--pet-shop',isCommercial:i%2===0,isCurrentlyFeatured:i===0,latitude:center[0]+(i%4-1)*.0016,longitude:center[1]+(Math.floor(i/4)-1)*.003,logoUrl:i%2?null:'https://fixture.invalid/logo.svg',detailsUrl:'/lokacije/'+(i+1)+'/testna-lokacija'}));
  const layer=new Function(build+';return buildManagedPlaceLayer;')()(places).addTo(fixtureMap);
  if(scene==='home-neighborhood'){const marker=layer.getLayers()[0];marker.openPopup();}
  for(let i=0;i<(scene==='home-bins'?18:3);i++)L.marker([center[0]+(i%6-2)*.0007,center[1]+(Math.floor(i/6)-1)*.0018],{icon:DoggyDropBinMarker.createIcon({})}).addTo(fixtureMap);
  if(active||scene==='home-navigation'||scene==='home-walk'){
   L.polyline([[46.5585,15.647],[46.5601,15.645],[46.562,15.6435],[46.5648,15.6425],[46.566,15.644]],{color:'#2f6d5f',weight:6,opacity:.9}).addTo(fixtureMap);
   L.marker([46.5648,15.6425],{icon:L.divIcon({className:'',html:'<span class="active-user-marker"><span></span></span>',iconSize:[34,34],iconAnchor:[17,17]})}).addTo(fixtureMap);
  }
 },{scene,zoom,build,isolatedId:isolated[scene]?.[1]});
 if(scene==='active-route')await page.evaluate(()=>{const panel=document.querySelector('.active-walk-cockpit');document.querySelector('.active-walk-page').style.setProperty('--walk-cockpit-height',Math.ceil(panel.getBoundingClientRect().height)+'px');});
 await page.addStyleTag({content:'html { scroll-behavior: auto !important; }'});
 await page.locator('.doggydrop-basemap').scrollIntoViewIfNeeded();
 await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
}
(async()=>{const browser=await chromium.launch({...(process.env.BROWSER_CHANNEL?{channel:process.env.BROWSER_CHANNEL}:{}),headless:true}),results=[];
 try {
 for(const width of [320,375,390,430,1024,1440]){
  const page=await browser.newPage({viewport:{width,height:900}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/*',async r=>{const u=new URL(r.request().url());
   if(u.hostname==='fixture.invalid')return r.fulfill({contentType:'image/svg+xml',body:'<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80"><rect width="80" height="80" rx="8" fill="#ffc928"/><text x="40" y="45" font-size="20" text-anchor="middle">PET</text></svg>'});
   if(['basemaps.cartocdn.com','tile.openstreetmap.org'].includes(u.hostname))return r.fulfill({contentType:'image/svg+xml',body:syntheticTile});
   if(u.hostname==='unpkg.com')return r.fulfill({path:old+(u.pathname.endsWith('.css')?'/leaflet.css':'/leaflet.js')});
   if(u.hostname==='cdn.jsdelivr.net')return r.fulfill({path:old+(u.pathname.endsWith('.woff2')?'/bootstrap-icons.woff2':'/bootstrap-icons.css')});
   if(u.hostname==='127.0.0.1'){
    const relative=decodeURIComponent(u.pathname).slice(1);
    let file=relative==='leaflet.js'?path.join(old,relative):relative==='DoggyDrop.styles.css'?asset('obj/Debug/net8.0/scopedcss/bundle/DoggyDrop.styles.css'):asset('wwwroot/'+relative);
    if(!path.resolve(file).startsWith(path.resolve(root)+path.sep)&&file!==path.join(old,'leaflet.js'))return r.abort();
    if(fs.existsSync(file)&&fs.statSync(file).isFile())return r.fulfill({path:file});
    return r.fulfill({status:404,body:''});
   }return r.abort();
  });
  for(const [scene,zoom]of scenes){await setup(page,scene,zoom);const problems=await page.evaluate(scene=>{
   const issues=[],map=document.querySelector('.doggydrop-basemap'),rect=map.getBoundingClientRect(),attr=map.querySelector('.leaflet-control-attribution');
   if(rect.width<200||rect.height<200)issues.push('map too small');
   if(document.documentElement.scrollWidth>innerWidth+1)issues.push('page horizontal overflow');
   const a=attr.getBoundingClientRect();if(a.left<0||a.right>innerWidth+1||a.top<rect.top||a.bottom>rect.bottom+1)issues.push('attribution outside map '+JSON.stringify({map:rect.toJSON(),attr:a.toJSON()}));
   if(parseFloat(getComputedStyle(attr).fontSize)<11)issues.push('attribution too small');
   for(const link of attr.querySelectorAll('a')){const r=link.getBoundingClientRect(),hit=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2);if(hit!==link&&!link.contains(hit))issues.push('attribution obscured: '+link.textContent);}
   if(attr.textContent.includes('Leaflet'))issues.push('framework prefix remains');
   if((scene!=='home-osm'&&!attr.textContent.includes('CARTO'))||!attr.textContent.includes('OpenStreetMap'))issues.push('missing credits');
   if(scene==='home-osm'&&attr.textContent.includes('CARTO'))issues.push('stale CARTO credit');
   const overlaps=(a,b)=>a.left<b.right&&a.right>b.left&&a.top<b.bottom&&a.bottom>b.top;
   for(const control of document.querySelectorAll('.leaflet-control-zoom,.leaflet-popup,.app-bottom-nav,.active-walk-cockpit,.active-walk-map-controls,.walk-planner-summary,.walk-planner-start')){
    const box=control.getBoundingClientRect();if(box.width&&box.height&&overlaps(a,box))issues.push('overlap: '+control.className);
   }
   if(map.dataset.basemap!==(scene==='home-osm'?'osm':'carto'))issues.push('unexpected fallback');return issues;
  },scene);
  if(scene==='admin-coordinate'||scene==='admin-create'){
   const point=await page.evaluate(()=>{fixtureMaps[0].fire('click',{latlng:L.latLng(46.557,15.646)});return [document.getElementById('Latitude').value,document.getElementById('Longitude').value];});
   if(Number(point[0])!==46.557||Number(point[1])!==15.646)problems.push('coordinate selection failed');
  }
  const links=page.locator('.doggydrop-basemap .leaflet-control-attribution a');
  // Prevent external navigation but exercise real mouse hit-testing and keyboard Tab focus.
  await links.first().evaluate(el=>{window.creditClicks=0;el.parentElement.addEventListener('click',e=>{e.preventDefault();window.creditClicks++;});});
  for(let i=0;i<await links.count();i++)await links.nth(i).click();
  if(await page.evaluate(()=>creditClicks)!==await links.count())problems.push('credit click failed');
  await links.first().focus();await page.keyboard.press('Tab');await page.keyboard.press('Shift+Tab');
  if(!await links.first().evaluate(el=>el===document.activeElement&&el.matches(':focus-visible')&&parseFloat(getComputedStyle(el).outlineWidth)>=2))problems.push('keyboard focus missing');
  await links.first().evaluate(el=>el.blur());
  if((scene==='home-city'&&[390,1024].includes(width))||(scene==='home-osm'&&width===390)||(scene==='active-route'&&width===390)||(scene==='admin-coordinate'&&width===1024))await page.screenshot({path:path.join(out,scene+'-'+width+'.png')});
  if(problems.length) { console.log(scene,width,await page.locator('.doggydrop-basemap').evaluate(el=>({map:el.getBoundingClientRect().toJSON(),attr:el.querySelector('.leaflet-control-attribution').getBoundingClientRect().toJSON()}))); await page.screenshot({path:path.join(out,'failure-'+scene+'-'+width+'.png')}); }
  if(errors.length)problems.push(...errors.splice(0));results.push({width,scene,problems});
  }
  await page.close();
 }
 fs.writeFileSync(out+'/layout-results.json',JSON.stringify(results,null,2));console.log(JSON.stringify({total:results.length,passed:results.filter(x=>!x.problems.length).length,failures:results.filter(x=>x.problems.length)},null,2));
 if(results.some(x=>x.problems.length))process.exitCode=1;
 } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1});
