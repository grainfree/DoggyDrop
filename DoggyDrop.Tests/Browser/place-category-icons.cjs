// Offline fixtures only. See docs/place-category-icons.md for required environment.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../..'),app=path.join(root,'DoggyDrop');
const capture=process.env.DOGGYDROP_POPUP_CAPTURE,assets=process.env.LEAFLET_TEST_ASSETS,out=process.env.PLACE_ICON_RESULTS;
if(!capture||!assets||!out)throw Error('Set DOGGYDROP_POPUP_CAPTURE, LEAFLET_TEST_ASSETS and PLACE_ICON_RESULTS.');
fs.mkdirSync(out,{recursive:true});
const read=p=>fs.readFileSync(p,'utf8'),clean=s=>s.replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi,'');
const home=clean(read(capture+'/home-anonymous.html'));
const source=read(app+'/Views/Map/Index.cshtml');
const build=source.slice(source.indexOf('        function buildManagedPlaceLayer('),source.indexOf('        function buildPlaceLayer('));
const definitions=[...read(app+'/Services/PlacePresentation.cs').matchAll(/new\(PlaceCategory\.(\w+), "([^"]+)", "[^"]+", "([^"]+)", "([^"]+)", (true|false)\)/g)]
 .map((m,i)=>({id:100+i,category:i+1,categoryLabel:m[2],categoryKey:m[3],iconClass:m[4],isCommercial:m[5]==='true',
 name:i===5?'Pasje igrišče Vir':m[2]+' · fixture',latitude:46.156105842,longitude:14.600444441,detailsUrl:'/Places/Details/'+(100+i)}));
const cases=[...definitions,
 {...definitions[1],id:201,name:'Mr.Pet · fixture logo',logoUrl:'https://fixture.invalid/logo.svg'},
 {...definitions[1],id:202,name:'Mr.Pet · broken logo',logoUrl:'https://fixture.invalid/broken.svg'},
 {...definitions[0],id:203,name:'Featured Veterinar',isCurrentlyFeatured:true},
 {...definitions[5],id:204,name:'Selected Pasje igrišče Vir',selected:true}];
const tile='<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256"><rect width="256" height="256" fill="#edf2e7"/><path d="M0 120H256M120 0V256" stroke="white" stroke-width="12"/></svg>';
const logo='<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80"><rect width="80" height="80" fill="#ffd22a"/><text x="40" y="45" text-anchor="middle" font-size="18">Mr.Pet</text></svg>';
(async()=>{
 const browser=await chromium.launch({channel:process.env.BROWSER_CHANNEL||'msedge',headless:true}),results=[],galleryResults=[];
 try{
 for(const width of [320,375,390,430,1024,1440])for(const dpr of [1,2]){
 const page=await browser.newPage({viewport:{width,height:900},deviceScaleFactor:dpr});const errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/*',r=>{
  const u=new URL(r.request().url());
  if(u.hostname==='fixture.invalid')return r.fulfill({status:u.pathname.includes('broken')?404:200,contentType:'image/svg+xml',body:u.pathname.includes('broken')?'':logo});
  if(['basemaps.cartocdn.com','tile.openstreetmap.org'].includes(u.hostname))return r.fulfill({contentType:'image/svg+xml',body:tile});
  if(u.hostname==='unpkg.com')return r.fulfill({path:assets+(u.pathname.endsWith('.css')?'/leaflet.css':'/leaflet.js')});
  if(u.hostname==='cdn.jsdelivr.net')return r.fulfill({path:assets+(u.pathname.endsWith('.woff2')?'/bootstrap-icons.woff2':'/bootstrap-icons.css')});
  if(u.hostname==='127.0.0.1'){
   if(u.pathname==='/')return r.fulfill({contentType:'text/html; charset=utf-8',body:home});
   if(u.pathname==='/surface'){
    let body=clean(read(capture+'/'+u.searchParams.get('name')+'.html'));
    if(u.searchParams.has('logo'))body=body.replace(new RegExp('https://res[.]cloudinary[.]com/[^" ]+','g'),'https://fixture.invalid/'+u.searchParams.get('logo')+'.svg');
    return r.fulfill({contentType:'text/html; charset=utf-8',body});
   }
   const file=path.join(app,'wwwroot',decodeURIComponent(u.pathname));
   if(file.startsWith(path.join(app,'wwwroot')+path.sep)&&fs.existsSync(file))return r.fulfill({path:file});
  }
  return r.abort();
 });
 await page.goto('http://127.0.0.1/');
 await page.addScriptTag({path:assets+'/leaflet.js'});
 for(const script of ['place-marker','place-popup','bin-marker'])await page.addScriptTag({path:app+'/wwwroot/js/'+script+'.js'});
 await page.evaluate(()=>new Promise((resolve,reject)=>{const s=document.createElement('script');s.src='/js/map-basemap.js';s.dataset.provider='carto';s.dataset.cartoApiKey='fixture-public-key';s.onload=resolve;s.onerror=reject;document.body.append(s);}));
 await page.evaluate(({build,cases})=>{
  window.map=L.map('map',{zoomAnimation:false,fadeAnimation:false}).setView([46.156105842,14.600444441],16);
  DoggyDropBasemap.addTo(map);
  map.createPane('placeMarkers').style.zIndex=610;
  window.attachManagedPlaceImage=DoggyDropPlaceMarker.attachImage;
  window.navigateToManagedPlaceInApp=id=>window.navigated=id;
  window.makeLayer=new Function(build+'; return buildManagedPlaceLayer;')();window.cases=cases;
 },{build,cases});
 await page.locator('#map').scrollIntoViewIfNeeded();
 for(let i=0;i<cases.length;i++){
  await page.evaluate(i=>{
   if(window.layer)map.removeLayer(layer);map.closePopup();map.setView([46.156105842,14.600444441],16);
   window.layer=makeLayer([cases[i]]).addTo(map);window.marker=layer.getLayers()[0];
   if(cases[i].selected)marker.setIcon(DoggyDropPlaceMarker.createIcon(cases[i],{selected:true}));
  },i);
  await page.waitForFunction(()=>[...document.querySelectorAll('.managed-place-image')].every(i=>i.complete));
  const normal=await page.locator('.managed-place-pin').evaluate(el=>({w:el.getBoundingClientRect().width,mask:getComputedStyle(el.querySelector('i')).maskImage,label:el.getAttribute('aria-label'),image:el.classList.contains('has-image')}));
  assert.match(normal.mask,/data:image\/svg\+xml/);assert.ok(normal.w>=42&&normal.w<=56);assert.ok(normal.label.includes(cases[i].name));
  assert.equal(normal.image,!!cases[i].logoUrl&&!cases[i].logoUrl.includes('broken'));
  // Real keyboard interaction opens the existing popup and selects the marker.
  await page.locator('.leaflet-marker-icon').focus();await page.keyboard.press('Enter');
  await page.locator('.managed-place-popup').waitFor({state:'visible'});
  await page.waitForFunction(()=>[...document.querySelectorAll('.managed-place-image')].every(i=>i.complete));
  const check=await page.evaluate(()=>{
   const pin=document.querySelector('.managed-place-pin'),popup=document.querySelector('.managed-place-popup'),icon=popup.querySelector('i');
   const rect=popup.getBoundingClientRect();return {selected:pin.classList.contains('managed-place-pin--selected'),mask:getComputedStyle(icon).maskImage,
    color:getComputedStyle(icon).color,hidden:icon.getAttribute('aria-hidden'),text:popup.textContent,image:popup.querySelector('.managed-place-popup__media').classList.contains('has-image'),
    within:rect.left>=0&&rect.right<=innerWidth+1,overflow:document.documentElement.scrollWidth>innerWidth+1,featured:!!popup.querySelector('.place-featured-badge')};
  });
  assert.ok(check.selected);assert.equal(check.mask,normal.mask);assert.equal(check.color,'rgb(36, 93, 72)');assert.equal(check.hidden,'true');
  assert.ok(check.text.includes(cases[i].categoryLabel));assert.ok(check.within);assert.equal(check.overflow,false);assert.equal(check.image,normal.image);
  assert.equal(check.featured,!!cases[i].isCurrentlyFeatured);
  await page.locator('.managed-place-popup__directions').click();assert.equal(await page.evaluate(()=>navigated),cases[i].id);
  if([390,1440].includes(width)&&dpr===1&&[1,5,6,8].includes(i))await page.screenshot({path:`${out}/map-${width}-${cases[i].id}.png`});
  results.push({width,dpr,case:cases[i].name,result:'PASS'});
 }
 // Gallery of actual normal/selected markers with shared CSS for visual comparison.
 await page.evaluate(cases=>{
  const box=document.createElement('section');box.id='icon-gallery';
  box.style='position:fixed;top:0;left:0;right:0;margin:auto;max-width:1000px;z-index:99999;background:#eef2e9;padding:20px;display:grid;gap:18px;align-content:start';
  box.style.gridTemplateColumns=`repeat(${innerWidth>=700?4:2},minmax(0,1fr))`;
  const heading=document.createElement('h2');heading.textContent='DoggyDrop · Final category icons';heading.style='grid-column:1/-1;margin:0;font-size:20px;color:#19352e';box.append(heading);
  for(const p of cases){const row=document.createElement('div');row.style='display:grid;gap:6px;justify-items:center;align-content:start;color:#19352e;padding:10px;border:1px solid #d2ddcf;border-radius:10px;background:#ffffff80';
   const name=document.createElement('strong');name.textContent=p.name;name.style='font-size:13px;text-align:center;min-height:36px';row.append(name);
   for(const selected of [false,true]){
    const icon=document.createElement('div');icon.innerHTML=DoggyDropPlaceMarker.createIcon(p,{selected}).options.html;
    const label=document.createElement('small');label.textContent=selected?'Selected':'Normal';label.style='font-size:10px;color:#53685e';
    row.append(icon,label);DoggyDropPlaceMarker.attachImage(icon);
   }
   box.append(row);
  }
  document.body.append(box);
 },cases);
 await page.waitForFunction(()=>[...document.querySelectorAll('#icon-gallery img')].every(i=>i.complete));
 assert.equal(await page.locator('#icon-gallery .dd-place-icon').count(),cases.length*2);
 if([390,1440].includes(width)){
  const height=await page.locator('#icon-gallery').evaluate(el=>Math.ceil(el.getBoundingClientRect().height));
  await page.setViewportSize({width,height:Math.max(900,height)});
  await page.locator('#icon-gallery').screenshot({path:`${out}/gallery-${width}-${dpr}x.png`});
 }
 const gallery=await page.locator('#icon-gallery .managed-place-pin').evaluateAll(els=>els.map(el=>({width:el.getBoundingClientRect().width,mask:getComputedStyle(el.querySelector('i')).maskImage})));
 for(const icon of gallery){assert.ok(icon.width>=42&&icon.width<=56);assert.match(icon.mask,/data:image\/svg\+xml/);}
 galleryResults.push({width,dpr,markers:gallery.length,result:'PASS'});

 assert.deepEqual(errors,[]);await page.close();
 }
 // Actual rendered public surfaces, no application startup or network.
 for(const width of [320,375,390,430,1024,1440]){
 const page=await browser.newPage({viewport:{width,height:900}});
 await page.route('**/*',r=>{
  const u=new URL(r.request().url());
  if(u.hostname==='127.0.0.1'){
   if(u.pathname==='/surface'){
    let body=clean(read(capture+'/'+u.searchParams.get('name')+'.html'));
    if(u.searchParams.has('logo'))body=body.replace(new RegExp('https://res[.]cloudinary[.]com/[^" ]+','g'),'https://fixture.invalid/'+u.searchParams.get('logo')+'.svg');
    return r.fulfill({contentType:'text/html; charset=utf-8',body});
   }
   const file=path.join(app,'wwwroot',decodeURIComponent(u.pathname));
   if(file.startsWith(path.join(app,'wwwroot')+path.sep)&&fs.existsSync(file))return r.fulfill({path:file});
  }
  if(u.hostname==='fixture.invalid')return r.fulfill({status:u.pathname.includes('broken')?404:200,contentType:'image/svg+xml',body:u.pathname.includes('broken')?'':logo});
  if(u.hostname==='cdn.jsdelivr.net')return r.fulfill({path:assets+(u.pathname.endsWith('.woff2')?'/bootstrap-icons.woff2':'/bootstrap-icons.css')});
  return r.abort();
 });
 for(const name of ['icons-discovery','icons-saved',...definitions.map(c=>'icons-details-'+c.categoryKey)]){
  await page.goto('http://127.0.0.1/surface?name='+name);
  const info=await page.locator('.dd-place-icon').evaluateAll(els=>els.map(el=>({mask:getComputedStyle(el).maskImage,w:el.getBoundingClientRect().width})));
  // Commercial Details retains text-only hierarchy when no logo/photo is present.
  for(const item of info){assert.match(item.mask,/data:image\/svg\+xml/);assert.ok(item.w>15);}
  if(['icons-discovery','icons-saved'].includes(name))assert.ok(info.length>=7);
  results.push({width,surface:name,result:'PASS'});
 }
 for(const state of ['logo','broken']){
  await page.goto('http://127.0.0.1/surface?name=icons-details-logo&logo='+state);
  await page.locator('.places-details__logo').scrollIntoViewIfNeeded();
  await page.addScriptTag({path:app+'/wwwroot/js/place-marker.js'});
  await page.addScriptTag({path:app+'/wwwroot/js/place-details.js'});
  await page.waitForFunction(()=>document.querySelector('.places-details__logo img').complete);
  const info=await page.locator('.places-details__logo').evaluate(el=>({loaded:el.classList.contains('has-image'),icon:getComputedStyle(el.querySelector('i')).display,image:getComputedStyle(el.querySelector('img')).opacity}));
  assert.equal(info.loaded,state==='logo');assert.equal(info.icon==='none',state==='logo');
  if(state==='logo')assert.equal(info.image,'1');
  results.push({width,surface:'Details '+state,result:'PASS'});
 }
 await page.close();
 }
 fs.writeFileSync(out+'/gallery-results.json',JSON.stringify(galleryResults,null,2));
 console.log(`Final gallery: ${galleryResults.reduce((sum,r)=>sum+r.markers,0)}/264 marker renders PASS.`);
 fs.writeFileSync(out+'/results.json',JSON.stringify(results,null,2));console.log(`${results.length}/${results.length} PASS: 132 Home marker/popup cases (11 variants x 6 widths x 2 DPR) + 54 public surface cases + 12 Details logo load/error cases.`);
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
