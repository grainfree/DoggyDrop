// Actual Razor + actual JS/Leaflet, all network intercepted. No live routing/tiles.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const capture=process.env.DOGGYDROP_SMART_CAPTURE,assets=process.env.LEAFLET_TEST_ASSETS,out=process.env.SMART_WALK_RESULTS;
if(!capture||!assets||!out)throw Error('Set DOGGYDROP_SMART_CAPTURE, LEAFLET_TEST_ASSETS, SMART_WALK_RESULTS');
fs.mkdirSync(out,{recursive:true});const web=path.resolve(__dirname,'../../DoggyDrop/wwwroot');
const tile='<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256"><rect width="256" height="256" fill="#edf2e7"/><path d="M0 100H256M130 0V256" stroke="white" stroke-width="8"/></svg>';
const result={token:'A'.repeat(48),points:[[46,15],[46.005,15.002],[46.004,15.009],[46,15]],distanceMeters:2250,durationSeconds:1800,
 facts:[{kind:'water',count:1,text:'1 pitnik ob poti'},{kind:'bin',count:2,text:'2 koša ob poti'},{kind:'park',count:1,text:'Ob poti: Pasji park'}],
 highlights:[{kind:'water',name:'Pitnik <img src=x onerror=alert(1)>',latitude:46.005,longitude:15.002}],notice:'Čas hoje je okviren. Upoštevaj označbe in razmere na terenu.'};
async function load(browser,width,provider,saved=false,largeText=false,state=null){
 const page=await browser.newPage({viewport:{width,height:width<768?844:900},reducedMotion:'reduce'}),errors=[],calls=[],posts=[];let fail=false,hold=false,pending=[];
 page.on('pageerror',e=>errors.push(e.message));
 await page.addInitScript(()=>{localStorage.setItem('pwaPromptShown','true');window.__geoMode='success';Object.defineProperty(navigator,'geolocation',{value:{getCurrentPosition:(fn,failed)=>{if(window.__geoMode==='denied')failed({code:1});else if(window.__geoMode==='hold')window.__geoSuccess=fn;else fn({coords:{latitude:46,longitude:15}});}}});});
 let html=fs.readFileSync(path.join(capture,state?'smart-'+state+'.html':saved?'smart-saved.html':'smart-initial.html'),'utf8');
 if(provider==='carto')html=html.replace('data-provider="osm"','data-provider="carto"').replace('data-carto-api-key=""','data-carto-api-key="fake-fixture-key"');
 await page.route('**/*',async r=>{
  const u=new URL(r.request().url());
  if(u.hostname==='unpkg.com')return r.fulfill({path:path.join(assets,u.pathname.endsWith('.css')?'leaflet.css':'leaflet.js')});
  if(u.hostname==='cdn.jsdelivr.net')return r.fulfill({path:path.join(assets,u.pathname.endsWith('.woff2')?'bootstrap-icons.woff2':'bootstrap-icons.css')});
  if(u.hostname.endsWith('tile.openstreetmap.org')||u.hostname==='basemaps.cartocdn.com')return r.fulfill({contentType:'image/svg+xml',body:tile});
  if(u.hostname==='127.0.0.1'){
   if(['/Walks/SmartPlan','/Walks/Start'].includes(u.pathname)){posts.push(r.request().postData());return r.fulfill({contentType:'text/html; charset=utf-8',body:'<h1>Sprehod je pripravljen</h1>'});}
   if(u.pathname==='/Walks/Planner'||u.pathname==='/Walks/Plan/1')return r.fulfill({contentType:'text/html; charset=utf-8',body:html});
   if(u.pathname==='/api/smart-walk'){calls.push(r.request().postDataJSON());if(hold)await new Promise(resolve=>pending.push(resolve));return r.fulfill(fail?{status:503,json:{error:'Pot trenutno ni na voljo. Poskusi znova.'}}:{json:result});}
   if(u.pathname==='/api/smart-walk/destinations')return r.fulfill({json:[{id:1,name:'Pasji park',category:'Pasji park'}]});
   if(u.pathname.startsWith('/api/'))return r.fulfill({json:[]});
   const file=path.resolve(web,'.'+decodeURIComponent(u.pathname));if(file.startsWith(web+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return r.fulfill({path:file});
  }
  return r.abort();
 });
 await page.goto('http://127.0.0.1/Walks/Planner');await page.waitForSelector('#smartMap.leaflet-container');
 assert.equal(await page.locator('#smartMap').getAttribute('data-basemap'),provider);
 // Emulate extra bottom safe-area occupancy without removing the established nav.
 if(width<768)await page.addStyleTag({content:'.app-bottom-nav{padding-bottom:34px!important} :root{--app-bottom-nav-clearance:124px!important}'});
 if(largeText)await page.addStyleTag({content:'html{font-size:125%!important}'});
 return{page,errors,calls,posts,setFail:v=>fail=v,setHold:v=>hold=v,release:()=>{pending.splice(0).forEach(f=>f());}};
}
async function layout(page,selector){
 await page.locator(selector).scrollIntoViewIfNeeded();
 await page.locator(selector).evaluate(e=>e.scrollIntoView({block:'center',behavior:'instant'}));
 const measured=await page.evaluate(selector=>{const b=document.querySelector(selector).getBoundingClientRect(),nav=document.querySelector('.app-bottom-nav'),n=nav?.getBoundingClientRect();return{overflow:document.documentElement.scrollWidth>innerWidth+1,bottom:b.bottom,top:b.top,navTop:n&&getComputedStyle(nav).display!=='none'?n.top:innerHeight};},selector);
  assert.equal(measured.overflow,false,'horizontal overflow');assert.ok(measured.top>=0,'above viewport');assert.ok(measured.bottom<=measured.navTop+1,'action obscured by bottom navigation: '+selector+' '+JSON.stringify(measured));
}
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});let layouts=0,lifecycle=0,savedCases=0,submissions=0,errorLayouts=0,xssCases=0;const measurements=[],geometryPerformance=[];
 try{
  for(const width of [320,375,390,430,768,1024,1440])for(const provider of ['osm','carto']){
   const f=await load(browser,width,provider),{page}=f;
   assert.equal(f.calls.length,0);await layout(page,'#smartGenerate');layouts++;
   if(width===390&&provider==='osm'){await page.evaluate(()=>scrollTo(0,0));await page.screenshot({path:path.join(out,'mobile-input.png'),fullPage:false});}
   await page.click('#smartGenerate');await page.waitForSelector('#smartError:not([hidden])');await layout(page,'#smartError');layouts++;assert.equal(f.calls.length,0);
   await page.click('#smartLocate');f.setHold(true);await page.click('#smartGenerate');await page.waitForFunction(()=>document.querySelector('#smartForm').getAttribute('aria-busy')==='true');
   assert.equal(await page.locator('#smartGenerate').isDisabled(),true);await page.evaluate(()=>document.querySelector('#smartForm').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true})));assert.equal(f.calls.length,1);lifecycle++;
   await layout(page,'#smartGenerate');layouts++;
   if(width===390&&provider==='osm')await page.screenshot({path:path.join(out,'mobile-loading.png'),fullPage:false});
   f.release();f.setHold(false);await page.waitForSelector('#smartResult:not([hidden])');
   assert.equal(await page.locator('#smartDuration').innerText(),'Približno 30 min');assert.equal(await page.locator('#smartFacts li').count(),3);assert.equal(await page.locator('#smartMap .smart-marker-start').count(),1);
   assert.equal(await page.locator('#smartMap .leaflet-overlay-pane path').count(),1);await layout(page,'#smartAgain');layouts++;
   assert.equal(await page.evaluate(()=>{const map=document.querySelector('#smartMap').getBoundingClientRect(),credit=document.querySelector('#smartMap .leaflet-control-attribution').getBoundingClientRect();return [...document.querySelectorAll('#smartMap .smart-marker')].every(e=>{const p=e.getBoundingClientRect();return p.left>=map.left&&p.right<=map.right&&p.top>=map.top&&p.bottom<=credit.top;});}),true,'markers clear of map edges and attribution');
   const links=page.locator('.smart-route-credit a');assert.equal(await links.count(),2);for(const a of await links.all()){await a.focus();assert.equal(await a.evaluate(e=>document.activeElement===e),true);}lifecycle++;
   if(provider==='osm'&&[390,1440].includes(width)){await page.locator('#smartResult').evaluate(e=>e.scrollIntoView({block:'start',behavior:'instant'}));await page.screenshot({path:path.join(out,width===390?'mobile-result.png':'desktop-result.png'),fullPage:false});}
   if(width===390){await page.locator('#smartMap .smart-marker-stop').first().click();assert.equal(await page.locator('.leaflet-popup-content img').count(),0);assert.ok((await page.locator('.leaflet-popup-content').innerText()).includes('<img'));await page.locator('.leaflet-popup-close-button').click();lifecycle++;}
   await page.click('#smartAgain');await page.waitForFunction(()=>document.querySelector('#smartForm').getAttribute('aria-busy')==='false');assert.equal(f.calls.length,2);assert.equal(f.calls[1].variant,1);assert.equal(await page.locator('#smartMap .leaflet-overlay-pane path').count(),1);lifecycle++;
   f.setFail(true);await page.click('#smartAgain');await page.waitForSelector('#smartError:not([hidden])');assert.equal(await page.locator('#smartGenerate').isDisabled(),false);await layout(page,'#smartError');layouts++;
   if(width===390&&provider==='osm')await page.screenshot({path:path.join(out,'mobile-error.png'),fullPage:false});
   f.setFail(false);await page.locator('#smartGenerate').focus();await page.keyboard.press('Enter');await page.waitForSelector('#smartResult:not([hidden])');assert.equal(f.calls.length,4);lifecycle++;
   await page.locator('input[value="destination"]').check();await page.fill('#smartSearch','park');await page.click('#smartFind');await page.waitForSelector('#smartPlace option[value="1"]',{state:'attached'});await page.selectOption('#smartPlace','1');await page.click('#smartGenerate');await page.waitForSelector('#smartResult:not([hidden])');assert.equal(f.calls.at(-1).placeId,1);await layout(page,'#smartAgain');layouts++;
   measurements.push({width,provider,requests:f.calls.length});assert.deepEqual(f.errors,[]);await page.close();
   const saved=await load(browser,width,provider,true);await saved.page.waitForSelector('#smartResult:not([hidden])');assert.equal(saved.calls.length,0);assert.equal(await saved.page.locator('#smartFreshActions').isVisible(),false);await layout(saved.page,'#smartSavedStart button');savedCases++;assert.deepEqual(saved.errors,[]);await saved.page.close();
  }
  for(const width of [320,390,768,1024]){
   const f=await load(browser,width,'osm',false,true);await f.page.click('#smartLocate');await f.page.click('#smartGenerate');await f.page.waitForSelector('#smartResult:not([hidden])');await layout(f.page,'#smartAgain');layouts++;
   await f.page.setViewportSize({width:844,height:390});await layout(f.page,'#smartAgain');layouts++;assert.deepEqual(f.errors,[]);await f.page.close();
  }
  for(const count of [250,1500,5000,20000]){
   const original=result.points;result.points=Array.from({length:count},(_,i)=>{const angle=i/(count-1)*Math.PI*2;return[46+.005*Math.sin(angle),15+.007*(1-Math.cos(angle))];});
   const f=await load(browser,390,'osm');await f.page.click('#smartLocate');const began=performance.now();await f.page.click('#smartGenerate');await f.page.waitForSelector('#smartResult:not([hidden])');
   assert.equal(await f.page.locator('#smartMap .leaflet-overlay-pane path').count(),1);assert.equal(f.calls.length,1);await layout(f.page,'#smartAgain');assert.deepEqual(f.errors,[]);geometryPerformance.push({vertices:count,renderMs:performance.now()-began});await f.page.close();result.points=original;
  }
  for(const mode of ['denied','hold']){
   const f=await load(browser,390,'osm');await f.page.evaluate(mode=>window.__geoMode=mode,mode);await f.page.click('#smartLocate');
   if(mode==='denied'){await f.page.waitForSelector('#smartError:not([hidden])');assert.equal(f.calls.length,0);}
   await f.page.click('#smartPick');await f.page.locator('#smartMap').focus();await f.page.keyboard.press('ArrowRight');await f.page.locator('#smartCenter').focus();await f.page.keyboard.press('Enter');
   if(mode==='hold')await f.page.evaluate(()=>window.__geoSuccess({coords:{latitude:48,longitude:16}}));
   await f.page.click('#smartGenerate');await f.page.waitForSelector('#smartResult:not([hidden])');assert.equal(f.calls.length,1);assert.ok(Math.abs(f.calls[0].start.latitude-46.1)<.1);assert.deepEqual(f.errors,[]);lifecycle++;await f.page.close();
  }
  for(const width of [320,375,390,430,768,1024])for(const intent of ['false','true','saved']){
   const f=await load(browser,width,'osm',intent==='saved'),{page}=f;
   if(intent!=='saved'){await page.click('#smartLocate');await page.click('#smartGenerate');await page.waitForSelector('#smartResult:not([hidden])');await page.selectOption('#smartDog','1');}
   const form=intent==='saved'?'#smartSavedStart':'#smartSave',button=intent==='saved'?form+' button':form+' button[value="'+intent+'"]';
   const submitStates=[];await page.exposeFunction('__submitState',state=>submitStates.push(state));
   await page.evaluate(form=>document.querySelector(form).addEventListener('submit',e=>window.__submitState({prevented:e.defaultPrevented,busy:e.currentTarget.getAttribute('aria-busy'),enabled:e.currentTarget.querySelectorAll('button:enabled').length})),form);
   if(width===768){await page.locator(button).focus();await page.keyboard.press('Enter');}
   else await page.evaluate(({form,button})=>{const f=document.querySelector(form),b=document.querySelector(button);f.requestSubmit(b);f.requestSubmit(b);},{form,button});
   await page.getByRole('heading',{name:'Sprehod je pripravljen'}).waitFor();
   assert.equal(f.posts.length,1);assert.equal(submitStates.filter(s=>!s.prevented).length,1);
   assert.ok(submitStates.every(s=>s.busy==='true'&&s.enabled===0));
   if(width!==768)assert.equal(submitStates.filter(s=>s.prevented).length,1);
   const data=new URLSearchParams(f.posts[0]);assert.ok(data.get('__RequestVerificationToken'));assert.equal(data.get('dogId'),'1');
   if(intent!=='saved'){assert.equal(data.get('start'),intent);assert.equal(data.get('token'),result.token);assert.equal(f.calls.length,1);}else assert.equal(f.calls.length,0);
   assert.equal(await page.locator('h1').innerText(),'Sprehod je pripravljen');assert.deepEqual(f.errors,[]);submissions++;await page.close();
  }
  for(const width of [320,375,390,430,768,1024,1440])for(const state of ['expired','unavailable']){
   const f=await load(browser,width,'osm',state==='unavailable',false,state);
   await layout(f.page,'#smartWalk > .smart-error');assert.equal(f.calls.length,0);errorLayouts++;
   assert.ok((await f.page.locator('#smartWalk > .smart-error').innerText()).includes(state==='expired'?'Predogled je potekel':'Postanek ni več'));
   if(width===390)await f.page.screenshot({path:path.join(out,'mobile-'+state+'.png'),fullPage:false});
   assert.deepEqual(f.errors,[]);await f.page.close();
  }
  for(const kind of ['water','park','place']){
   const oldHighlights=result.highlights,oldFacts=result.facts,hostile='Zapri </script><img src=x onerror="window.__xss=1">';
   result.highlights=[{kind,name:hostile,latitude:46.005,longitude:15.002}];result.facts=[{kind,count:1,text:hostile}];
   const f=await load(browser,390,'osm');await f.page.click('#smartLocate');await f.page.click('#smartGenerate');await f.page.waitForSelector('#smartResult:not([hidden])');
   assert.ok((await f.page.locator('#smartFacts').innerText()).includes(hostile));assert.equal(await f.page.locator('#smartFacts img, #smartFacts script').count(),0);
   await f.page.locator('.smart-marker-stop').click();assert.ok((await f.page.locator('.leaflet-popup-content').innerText()).includes(hostile));assert.equal(await f.page.locator('.leaflet-popup-content img,.leaflet-popup-content script').count(),0);assert.equal(await f.page.evaluate(()=>window.__xss),undefined);
   assert.deepEqual(f.errors,[]);xssCases++;await f.page.close();result.highlights=oldHighlights;result.facts=oldFacts;
  }
  const report={layouts,lifecycle,savedCases,submissions,errorLayouts,xssCases,measurements,geometryPerformance};fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
