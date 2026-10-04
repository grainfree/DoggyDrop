const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const web=path.resolve(__dirname,'../../DoggyDrop/wwwroot');
const worker=fs.readFileSync(path.join(web,'sw.js'),'utf8');
const pwa=fs.readFileSync(path.join(web,'js/pwa.js'),'utf8');
function sw(){
 const events={},stores=new Map(),network={offline:false,body:'USER_A'},calls=[];
 const caches={keys:async()=>[...stores.keys()],delete:async k=>stores.delete(k),open:async k=>{
  if(!stores.has(k))stores.set(k,new Map());const store=stores.get(k);
  return {addAll:async urls=>urls.forEach(u=>store.set(u,'PUBLIC:'+u)),match:async u=>store.get(u)};
 }};
 vm.runInNewContext(worker,{self:{location:{origin:'https://local.invalid'},addEventListener:(k,fn)=>events[k]=fn},caches,URL,
  fetch:async request=>{calls.push(request);if(network.offline)throw Error('offline');return network.body;}});
 async function lifecycle(name){let result;events[name]({waitUntil:p=>result=p});await result;}
 async function request(url,method='GET',mode='navigate'){let result,intercepted=false;events.fetch({request:{url:'https://local.invalid'+url,method,mode},respondWith:p=>{intercepted=true;result=p;}});return {intercepted,body:await result};}
 return {events,stores,network,calls,lifecycle,request};
}
test('worker precaches only public first-party offline assets',async()=>{const s=sw();await s.lifecycle('install');assert.deepEqual([...s.stores.values()][0].size,2);assert.deepEqual([...s.stores.values()][0].keys().toArray(),['/offline.html','/css/pwa.css']);});
for(const url of ['/Home/UserProfile','/Dogs','/Walks','/SavedPlans','/BinContributions/Mine','/Identity/Account/Manage','/AdminBins'])
 test('A/logout/B/offline never replays private '+url,async()=>{const s=sw();await s.lifecycle('install');assert.equal((await s.request(url)).body,'USER_A');s.network.body='USER_B';assert.equal((await s.request(url)).body,'USER_B');s.network.offline=true;assert.equal((await s.request(url)).body,'PUBLIC:/offline.html');assert.equal([...s.stores.values()][0].size,2);});
for(const url of ['/Identity/Account/Manage/ChangePassword','/api/confirmations','/BinContributions/Create','/Walks/AddPoint','/AdminBins/Edit'])
 test('worker does not intercept or replay POST '+url,async()=>{const s=sw();s.network.offline=true;assert.equal((await s.request(url,'POST')).intercepted,false);assert.equal(s.calls.length,0);});
test('private APIs, tiles and other runtime assets bypass SW',async()=>{const s=sw();for(const url of ['/api/private','/js/site.js?v=hash','/uploads/photo.png','/api/walking-routes'])assert.equal((await s.request(url,'GET','cors')).intercepted,false);let called=false;s.events.fetch({request:{url:'https://tile.openstreetmap.org/1/1/1.png',method:'GET',mode:'cors'},respondWith:()=>called=true});assert.equal(called,false);});
test('activation removes only old DoggyDrop offline caches',async()=>{const s=sw();await s.lifecycle('install');s.stores.set('doggydrop-offline-old',new Map());s.stores.set('unrelated-cache',new Map());await s.lifecycle('activate');assert.deepEqual([...s.stores.keys()],['doggydrop-offline-v1','unrelated-cache']);});
test('offline recovers with fresh network and never stores navigation',async()=>{const s=sw();await s.lifecycle('install');s.network.offline=true;assert.equal((await s.request('/')).body,'PUBLIC:/offline.html');s.network.offline=false;s.network.body='FRESH';assert.equal((await s.request('/')).body,'FRESH');assert.equal([...s.stores.values()][0].size,2);});
test('no forced worker activation, page reload or background replay',()=>{const code=worker.replace(/\/\/[^\n]*/g,'');assert.doesNotMatch(code,/skipWaiting|clients\.claim|addEventListener\('sync'|postMessage/);assert.doesNotMatch(pwa,/location\.(reload|replace|assign)|setInterval|watchPosition|requestPermission|localStorage|sessionStorage/);});
function client({installed=false,ios=false,online=true}={}){
 const handlers={},els=Object.fromEntries(['pwaInstall','pwaInstallButton','pwaInstallHelp','pwaUpdate','pwaNetwork','pwaNetworkDismiss'].map(id=>[id,{hidden:true,disabled:false,handlers:{},addEventListener(k,f){this.handlers[k]=f;}}]));
 const navigator={onLine:online,standalone:installed,userAgent:ios?'iPhone':'Chromium',maxTouchPoints:0};
 vm.runInNewContext(pwa,{window:{matchMedia:()=>({matches:installed}),isSecureContext:true,addEventListener:(k,f)=>handlers[k]=f},navigator,document:{getElementById:id=>els[id]}});
 return {handlers,els,navigator};
}
test('install is explicit, unsupported browsers get guidance',()=>{const c=client();assert.equal(c.els.pwaInstallButton.hidden,true);assert.equal(c.els.pwaInstall.hidden,false);assert.match(c.els.pwaInstallHelp.textContent,/brez namestitve/);});
test('standalone never offers install',()=>{const c=client({installed:true});assert.equal(c.els.pwaInstall.hidden,true);assert.equal(c.els.pwaInstallButton.hidden,true);});
test('iOS receives accurate contextual Share guidance',()=>{assert.match(client({ios:true}).els.pwaInstallHelp.textContent,/Deli.*Dodaj na začetni zaslon/);});
test('install request requires click and prompt can only be used once',async()=>{const c=client();let calls=0,prevented=0;c.handlers.beforeinstallprompt({preventDefault(){prevented++;},prompt:async()=>calls++,userChoice:Promise.resolve({outcome:'dismissed'})});assert.equal(calls,0);assert.equal(prevented,1);assert.equal(c.els.pwaInstallButton.hidden,false);await Promise.all([c.els.pwaInstallButton.handlers.click(),c.els.pwaInstallButton.handlers.click()]);assert.equal(calls,1);assert.equal(c.els.pwaInstallButton.hidden,true);assert.equal(c.els.pwaInstallButton.disabled,false);});
test('failed install leaves usable menu and no nag loop',async()=>{const c=client();c.handlers.beforeinstallprompt({preventDefault(){},prompt:async()=>{throw Error('cancel');}});await c.els.pwaInstallButton.handlers.click();assert.equal(c.els.pwaInstallButton.disabled,false);assert.equal(c.els.pwaInstallButton.hidden,true);});
test('offline message dismisses and online recovery does not submit/reload',()=>{const c=client({online:false});assert.equal(c.els.pwaNetwork.hidden,false);c.els.pwaNetworkDismiss.handlers.click();assert.equal(c.els.pwaNetwork.hidden,true);c.handlers.offline();assert.equal(c.els.pwaNetwork.hidden,false);c.navigator.onLine=true;c.handlers.online();assert.equal(c.els.pwaNetwork.hidden,true);});
test('manifest and PNG dimensions agree; icons not falsely maskable',()=>{const m=JSON.parse(fs.readFileSync(path.join(web,'manifest.json')));assert.equal(m.name,'DoggyDrop');assert.equal(m.start_url,'/');assert.equal(m.scope,'/');assert.equal(m.display,'standalone');assert.equal(m.theme_color,'#2F6D5F');for(const icon of m.icons){const data=fs.readFileSync(path.join(web,icon.src));assert.equal(data.readUInt32BE(16)+'x'+data.readUInt32BE(20),icon.sizes);assert.equal(icon.purpose,'any');}const apple=fs.readFileSync(path.join(web,'images/icon-180.png'));assert.equal(apple.readUInt32BE(16),180);assert.equal(apple.readUInt32BE(20),180);});
