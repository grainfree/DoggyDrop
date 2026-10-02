const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const code=fs.readFileSync(path.resolve(__dirname,'../../DoggyDrop/wwwroot/js/bin-contributions.js'),'utf8');
function fixture(valid=true) {
 const elements=new Map(),windowEvents={},requests=[],navigations=[];
 const summary={classList:{add(){},remove(){}},replaceChildren(list){this.messages=list.children.map(x=>x.textContent);},focus(){this.focused=true;}};
 for(const id of ['contributionForm','contributionSubmit','contributionSubmitLabel','contributionSpinner','contributionSubmitStatus'])
  elements.set(id,{events:{},attributes:{},disabled:false,hidden:true,textContent:'',addEventListener(k,f){this.events[k]=f;},setAttribute(k,v){this.attributes[k]=v;}});
 const form=elements.get('contributionForm');form.checkValidity=()=>valid;form.reportValidity=()=>{form.reported=true;};
 form.action='/BinContributions/Create';form.dataset={successUrl:'/BinContributions/Mine'};form.elements=[];form.querySelectorAll=()=>[];
 form.fields={RequestId:'same-request-id',Photo:'selected-photo',Description:'Entered text',Latitude:'46',Longitude:'15',__RequestVerificationToken:'real-form-token'};
 const context={document:{getElementById:id=>elements.get(id),querySelector:()=>summary,createElement:()=>({children:[],appendChild(x){this.children.push(x);}})},
  window:{addEventListener(k,f){windowEvents[k]=f;},location:{assign(url){navigations.push(url);}}},
  FormData:class{constructor(f){this.values={...f.fields};}},
  DOMParser:class{parseFromString(){return{querySelectorAll:selector=>selector.includes(' li')?[{textContent:'Server validation message'}]:[]};}},
  fetch:(url,options)=>new Promise((resolve,reject)=>requests.push({url,options,resolve,reject}))};
 vm.runInNewContext(code,context);
 return {elements,form,windowEvents,requests,navigations,summary,submit(cancelled=false){const event={defaultPrevented:cancelled,preventDefault(){this.defaultPrevented=true;}};return{event,promise:form.events.submit(event)};}};
}
const response=(status=400)=>({status,type:'basic',headers:{get:()=>status===400?'text/html':null},text:async()=>'<server validation>'});
for(const kind of ['photo','issue','location'])test(`${kind}: first submit synchronously enters busy state and posts original fields/token once`,()=>{
 const f=fixture(),before={...f.form.fields};const {event}=f.submit();assert.equal(event.defaultPrevented,true);
 assert.equal(f.requests.length,1);assert.equal(f.elements.get('contributionSubmit').disabled,true);
 assert.equal(f.elements.get('contributionSubmitLabel').textContent,'Pošiljam …');assert.equal(f.elements.get('contributionSpinner').hidden,false);
 assert.equal(f.form.attributes['aria-busy'],'true');assert.deepEqual(f.form.fields,before);
 assert.deepEqual(f.requests[0].options.body.values,before);assert.equal(f.requests[0].options.credentials,'same-origin');
 assert.equal(f.requests[0].options.redirect,'manual');assert.match(f.elements.get('contributionSubmitStatus').textContent,/Pošiljam/);
});
test('rapid repeated submission produces one client request',()=>{
 const f=fixture();f.submit();f.submit();f.submit();assert.equal(f.requests.length,1);
});
test('invalid input retains validation and never enters busy state',()=>{
 const f=fixture(false);f.submit();assert.equal(f.form.reported,true);assert.equal(f.requests.length,0);
 assert.equal(f.elements.get('contributionSubmit').disabled,false);assert.equal(f.form.attributes['aria-busy'],'false');
});
test('previously cancelled submission does not lock or send the form',()=>{
 const f=fixture();f.submit(true);assert.equal(f.elements.get('contributionSubmit').disabled,false);assert.equal(f.requests.length,0);
});
test('server validation restores controls, retains photo/text/RequestId and allows retry',async()=>{
 const f=fixture(),before={...f.form.fields};const run=f.submit();f.requests[0].resolve(response());await run.promise;
 assert.deepEqual(f.summary.messages,['Server validation message']);assert.equal(f.summary.focused,true);
 assert.equal(f.elements.get('contributionSubmit').disabled,false);assert.equal(f.form.attributes['aria-busy'],'false');
 assert.equal(f.elements.get('contributionSubmitLabel').textContent,'Pošlji v pregled');assert.equal(f.elements.get('contributionSpinner').hidden,true);
 assert.deepEqual(f.form.fields,before);f.submit();assert.equal(f.requests.length,2);assert.deepEqual(f.requests[1].options.body.values,before);
});
for(const status of [429,500])test(`HTTP ${status} restores controls with readable retry feedback`,async()=>{
 const f=fixture();const run=f.submit();f.requests[0].resolve(response(status));await run.promise;
 assert.equal(f.elements.get('contributionSubmit').disabled,false);assert.equal(f.summary.messages.length,1);assert.equal(f.navigations.length,0);
});
test('network failure retains selection and permits a same-key retry',async()=>{
 const f=fixture(),before={...f.form.fields};const run=f.submit();f.requests[0].reject(new Error('offline'));await run.promise;
 assert.equal(f.form.attributes['aria-busy'],'false');assert.match(f.summary.messages[0],/Povezava/);assert.deepEqual(f.form.fields,before);
 f.submit();assert.equal(f.requests[1].options.body.values.RequestId,before.RequestId);
});
test('success navigates to existing Mine flow without consuming its TempData in fetch',async()=>{
 const f=fixture();const run=f.submit();f.requests[0].resolve({type:'opaqueredirect'});await run.promise;
 assert.deepEqual(f.navigations,['/BinContributions/Mine']);assert.equal(f.elements.get('contributionSubmit').disabled,true);
});
test('browser-back restores controls and late old response cannot overwrite newer submission',async()=>{
 const f=fixture();const old=f.submit();f.windowEvents.pageshow({persisted:true});
 assert.equal(f.form.attributes['aria-busy'],'false');f.submit();
 f.requests[0].resolve({type:'opaqueredirect'});await old.promise;
 assert.equal(f.navigations.length,0);assert.equal(f.form.attributes['aria-busy'],'true');assert.equal(f.requests.length,2);
});
