// Real Razor captures and local assets only; every external request is aborted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const capture=process.env.DOGGYDROP_PRIVACY_CAPTURE,out=process.env.DOGGYDROP_PRIVACY_RESULTS;
if(!capture||!out)throw Error('Set external privacy capture/results directories');
const web=path.resolve(__dirname,'../../DoggyDrop/wwwroot');
fs.mkdirSync(out,{recursive:true});
(async()=>{
 const browser=await chromium.launch({channel:process.env.BROWSER_CHANNEL||'msedge',headless:true});const results=[];
 try{
  for(const width of [320,375,390,430,768,1024,1440])for(const height of [900,430])
  for(const safe of [0,34])for(const scene of ['privacy','terms','personal-data','delete-data']){
   const page=await browser.newPage({viewport:{width,height}}),errors=[];
   page.on('pageerror',e=>errors.push(e.message));
   await page.route('**/*',route=>{
    const u=new URL(route.request().url());
    if(u.hostname!=='127.0.0.1')return route.abort();
    if(u.pathname==='/')return route.fulfill({contentType:'text/html; charset=utf-8',body:fs.readFileSync(path.join(capture,scene+'.html'),'utf8')});
    if(u.pathname.startsWith('/api/'))return route.fulfill({json:{items:[],unreadCount:0}});
    const f=path.resolve(web,'.'+u.pathname);
    if(f.startsWith(web+path.sep)&&fs.existsSync(f)&&fs.statSync(f).isFile())return route.fulfill({path:f});
    return route.abort();
   });
   await page.goto('http://127.0.0.1/');
   if(safe)await page.addStyleTag({content:':root{--app-bottom-nav-clearance:122px}.app-bottom-nav{bottom:46px}'});
   for(const scale of [1,1.25]){
    await page.addStyleTag({content:`html{font-size:${16*scale}px}`});
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`${scene} ${width}: overflow`);
    const content=page.locator('.legal-page');assert.ok(await content.isVisible());
    assert.ok(await content.locator('h1').isVisible());
    for(const control of await content.locator('a,button,input:not([type=hidden])').all()){
     if(!await control.isVisible())continue;
     await control.focus();assert.ok(await control.evaluate(e=>document.activeElement===e));
     await control.evaluate(e=>{
      e.scrollIntoView({block:'center',behavior:'instant'});
      const n=document.querySelector('.app-bottom-nav'),b=n&&getComputedStyle(n).display!=='none'?n.getBoundingClientRect().top:innerHeight;
      if(e.getBoundingClientRect().bottom>b-12)scrollBy({top:e.getBoundingClientRect().bottom-b+12,behavior:'instant'});
     });
     assert.ok(await control.evaluate(e=>{
      const n=document.querySelector('.app-bottom-nav'),b=n&&getComputedStyle(n).display!=='none'?n.getBoundingClientRect().top:innerHeight;
      return e.getBoundingClientRect().bottom<=b+1;
     }),`${scene} ${width}: bottom navigation overlap`);
    }
    for(const link of await content.locator('a[href^="#"]').all()){
     const href=await link.getAttribute('href');assert.equal(await page.locator(href).count(),1);
    }
    assert.deepEqual(errors,[]);results.push({scene,width,height,safe,scale});
   }
   if(width===390&&height===900&&safe===34)await page.screenshot({path:path.join(out,scene+'-390.png'),fullPage:true});
   await page.close();
  }
 }finally{await browser.close();}
 fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({passed:results.length,results},null,2));
 console.log(`Privacy layouts, 125% text, focus and safe-area: ${results.length}/${results.length} PASS`);
})().catch(e=>{console.error(e);process.exit(1)});
