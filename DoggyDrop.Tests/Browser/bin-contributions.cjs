// Captured local Razor pages, synthetic map/photo responses; no production or provider traffic.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const capture=process.env.DOGGYDROP_COMMUNITY_CAPTURE,assets=process.env.LEAFLET_TEST_ASSETS,out=process.env.BIN_COMMUNITY_RESULTS;
if(!capture||!assets||!out)throw Error('Set DOGGYDROP_COMMUNITY_CAPTURE, LEAFLET_TEST_ASSETS, BIN_COMMUNITY_RESULTS');
const web=path.resolve(__dirname,'../../DoggyDrop/wwwroot');fs.mkdirSync(out,{recursive:true});
const tile='<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256"><rect width="256" height="256" fill="#edf2e7"/></svg>';
async function load(browser,file,width){
 const page=await browser.newPage({viewport:{width,height:900}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.addInitScript(()=>{localStorage.setItem('doggydrop.homeIntroDismissed.v1','true');window.geoCalls=0;Object.defineProperty(navigator,'geolocation',{value:{getCurrentPosition:()=>window.geoCalls++,watchPosition:()=>1,clearWatch:()=>{}}});});
 await page.route('**/*',r=>{const u=new URL(r.request().url());
  if(u.hostname==='unpkg.com')return r.fulfill({path:assets+(u.pathname.endsWith('.css')?'/leaflet.css':'/leaflet.js')});
  if(u.hostname==='cdn.jsdelivr.net')return r.fulfill({path:assets+(u.pathname.endsWith('.woff2')?'/bootstrap-icons.woff2':'/bootstrap-icons.css')});
  if(u.hostname.endsWith('tile.openstreetmap.org')||u.hostname==='fixture.invalid')return r.fulfill({contentType:'image/svg+xml',body:tile});
  if(u.hostname==='127.0.0.1'){
   if(u.pathname==='/')return r.fulfill({contentType:'text/html; charset=utf-8',body:fs.readFileSync(capture+'/'+file+'.html','utf8')});
   if(u.pathname.startsWith('/uploads/'))return r.fulfill({contentType:'image/svg+xml',body:tile});
   if(u.pathname.startsWith('/api/'))return r.fulfill({json:{items:[],hotspots:[],events:[]}});
   const p=path.resolve(web,'.'+u.pathname);if(p.startsWith(web+path.sep)&&fs.existsSync(p)&&fs.statSync(p).isFile())return r.fulfill({path:p});
  }return r.abort();});
 await page.goto('http://127.0.0.1/');return{page,errors};
}
(async()=>{const browser=await chromium.launch({channel:process.env.BROWSER_CHANNEL||'msedge',headless:true}),results=[];let actionCases=0;
 try{
  for(const width of [320,375,390,430,1024,1440]){
   const {page,errors}=await load(browser,'home',width);await page.waitForFunction(()=>typeof binLayer!=='undefined'&&binLayer);
   if(width===390){
    const requests=[];
    await page.route('**/Map/BinAction/*',r=>{
     requests.push({url:r.request().url(),method:r.request().method(),body:r.request().postData()});
     return r.fulfill({json:{id:1,usedCount:1,fullReports:0,missingReports:0,usefulVotes:0,notUsefulVotes:0}});
    });
    const token=await page.evaluate(()=>requestVerificationToken);
    assert.ok(token);
    for(const action of ['used','full','useful','not-useful']){
     await page.evaluate(action=>sendBinAction(1,action),action);
     const request=requests.at(-1),body=new URLSearchParams(request.body);
     assert.equal(request.method,'POST');assert.equal(new URL(request.url).search,'');
     assert.equal(body.get('binAction'),action);assert.equal(body.get('__RequestVerificationToken'),token);actionCases++;
    }
    assert.equal(requests.length,4);
   }
   for(const photo of [false,true])for(const long of [false,true]){
    await page.evaluate(({photo,long})=>{bins[0].image=photo?'https://fixture.invalid/photo.svg':null;bins[0].sourceName=long?'OpenStreetMap — koši za pasje iztrebke · '+ 'Dolgo ime vira '.repeat(8):'OpenStreetMap';openBinDetail(bins[0]);},{photo,long});
    const panel=page.locator('#binDetailPanel');await panel.waitFor({state:'visible'});
    assert.equal(await panel.locator('a[href*="?photo=true"]').innerText(),photo?'Predlagaj novo fotografijo':'Dodaj fotografijo');
    assert.ok((await panel.locator('a[href*="?photo=true"]').getAttribute('href')).includes('/1?photo=true'));
    assert.equal(await panel.locator('a', {hasText:'Prijavi težavo'}).count(),1);
    assert.equal(await panel.getByRole('button',{name:'Navigacija',exact:true}).count(),1);
    assert.ok(await panel.evaluate(e=>e.scrollWidth<=e.clientWidth+2),'panel horizontal overflow');
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+2),'page horizontal overflow');
    if(width===390&&!photo&&long)await page.screenshot({path:out+'/home-no-photo-390.png',fullPage:true});
    results.push({width,photo,long});
   }assert.deepEqual(errors,[]);await page.close();
   for(const file of ['contribute','photo','admin-review','admin-queue','admin-lifecycle']){
    const {page,errors}=await load(browser,file,width);assert.equal(await page.evaluate(()=>geoCalls),0);
    if(file==='contribute'){await page.locator('#contributionReason').selectOption('WRONG_LOCATION');assert.equal(await page.evaluate(()=>geoCalls),0);await page.locator('#contributionLocate').click();assert.equal(await page.evaluate(()=>geoCalls),1);}
    const overflow=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,elements:[...document.querySelectorAll('body *')].filter(e=>e.getBoundingClientRect().right>innerWidth+2).slice(0,8).map(e=>({tag:e.tagName,id:e.id,cls:e.className,right:e.getBoundingClientRect().right}))}));
    if(overflow.scroll>width+2)await page.screenshot({path:out+'/overflow-'+file+'-'+width+'.png',fullPage:true});
    assert.ok(overflow.scroll<=width+2,JSON.stringify({file,...overflow}));assert.deepEqual(errors,[]);
    if(width===390)await page.screenshot({path:out+'/'+file+'-390.png',fullPage:true});results.push({width,file});await page.close();
   }
  }
  fs.writeFileSync(out+'/results.json',JSON.stringify(results,null,2));console.log(`PASS ${results.length}/${results.length} community layout/entry/geolocation cases`);
  console.log(`PASS ${actionCases}/4 actual Home mutation request cases`);
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
