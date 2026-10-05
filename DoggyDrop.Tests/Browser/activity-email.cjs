// Actual Razor/template captures; no external/provider requests are permitted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const capture=process.env.DOGGYDROP_EMAIL_CAPTURE,out=process.env.DOGGYDROP_EMAIL_RESULTS;
if(!capture||!out)throw Error('Set external email capture/results paths');
const web=path.resolve(__dirname,'../../DoggyDrop/wwwroot');fs.mkdirSync(out,{recursive:true});
(async()=>{
 const browser=await chromium.launch({channel:process.env.BROWSER_CHANNEL||'msedge',headless:true}),results=[];
 try{
  for(const scene of ['preferences-default','preferences-saved','admin-email'])
  for(const width of [320,375,390,430,768,1024,1440])for(const height of [900,430])for(const safe of [0,34]){
   const page=await browser.newPage({viewport:{width,height}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
   await page.route('**/*',route=>{
    const u=new URL(route.request().url());if(u.hostname!=='127.0.0.1')return route.abort();
    if(u.pathname==='/')return route.fulfill({contentType:'text/html; charset=utf-8',body:fs.readFileSync(path.join(capture,scene+'.html'),'utf8')});
    if(u.pathname.startsWith('/api/'))return route.fulfill({json:{items:[],unreadCount:0}});
    const file=path.resolve(web,'.'+u.pathname);if(file.startsWith(web+path.sep)&&fs.existsSync(file)&&fs.statSync(file).isFile())return route.fulfill({path:file});return route.abort();
   });
   await page.goto('http://127.0.0.1/');
   if(safe)await page.addStyleTag({content:'.email-settings{padding-bottom:156px}.app-bottom-nav{bottom:46px}'});
   for(const scale of [1,1.25]){
    await page.addStyleTag({content:`html{font-size:${16*scale}px}`});
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`${scene}/${width}: overflow`);
    for(const control of await page.locator('.email-settings a,.email-settings button,.email-settings select,.email-settings input:not([type=hidden])').all()){
     await control.focus();assert.ok(await control.evaluate(e=>document.activeElement===e));
     await control.evaluate(e=>{e.scrollIntoView({block:'center',behavior:'instant'});const nav=document.querySelector('.app-bottom-nav'),top=nav&&getComputedStyle(nav).display!=='none'?nav.getBoundingClientRect().top:innerHeight;scrollBy({top:Math.max(0,e.getBoundingClientRect().bottom-top+12),behavior:'instant'});});
     assert.ok(await control.evaluate(e=>{const n=document.querySelector('.app-bottom-nav');return e.getBoundingClientRect().bottom<=(n&&getComputedStyle(n).display!=='none'?n.getBoundingClientRect().top:innerHeight)+1;}),`${scene}/${width}: overlap`);
    }
    if(scene.startsWith('preferences')){assert.equal(await page.locator('input[type=checkbox]').count(),1);assert.equal(await page.locator('label input[type=checkbox]').count(),1);assert.ok(await page.locator('#email-help').isVisible());}
    assert.deepEqual(errors,[]);results.push({scene,width,height,safe,scale});
   }
   if(width===390&&height===900&&safe===34){await page.evaluate(()=>scrollTo({top:0,behavior:'instant'}));await page.screenshot({path:path.join(out,scene+'-390.png'),fullPage:true});}await page.close();
  }
  for(const file of fs.readdirSync(capture).filter(f=>f.startsWith('template-')&&f.endsWith('.html')))
  for(const width of [320,375,390,430,768,1024,1440])for(const scale of [1,1.25]){
   const page=await browser.newPage({viewport:{width,height:900}});await page.route('**/*',r=>r.abort());
   await page.setContent(fs.readFileSync(path.join(capture,file),'utf8'));await page.addStyleTag({content:`body{font-size:${16*scale}px}`});
   assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`${file}/${width}: overflow`);
   assert.equal(await page.locator('script,img,iframe').count(),0);assert.ok(await page.locator('h1').isVisible());
   for(const a of await page.locator('a').all()){assert.ok((await a.getAttribute('href')).startsWith('https://doggydrop.example/'));await a.focus();assert.ok(await a.evaluate(e=>document.activeElement===e));}
   results.push({scene:file,width,scale});await page.close();
  }
 }finally{await browser.close();}
 fs.writeFileSync(path.join(out,'results.json'),JSON.stringify({passed:results.length,results},null,2));console.log(`Email preferences/Admin/templates: ${results.length}/${results.length} PASS`);
})().catch(e=>{console.error(e);process.exit(1)});
