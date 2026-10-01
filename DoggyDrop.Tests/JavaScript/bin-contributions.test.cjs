const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const code=fs.readFileSync(path.resolve(__dirname,'../../DoggyDrop/wwwroot/js/bin-contributions.js'),'utf8');
function fixture(photo=false){
 const elements=new Map();for(const id of ['contributionReason','locationProposal','proposalLatitude','proposalLongitude','contributionLocationStatus','duplicateProposal','contributionLocate'])elements.set(id,{value:'',hidden:true,dataset:{lat:'46',lon:'15'},events:{},addEventListener(k,f){this.events[k]=f;}});
 const calls={geo:0,maps:0,tiles:0};const map={events:{},setView(){return this;},on(k,f){this.events[k]=f;return this;},invalidateSize(){}};
 const L={map(){calls.maps++;return map;},circleMarker(){return{addTo(){return this;},bindPopup(){return this;}};},marker(point){return{point,addTo(){return this;},on(){return this;},setLatLng(p){this.point=p;},getLatLng(){return{lat:this.point[0],lng:this.point[1]};}};}};
 const context={document:{getElementById:id=>photo&&id==='contributionReason'?null:elements.get(id)},window:{isSecureContext:true},navigator:{geolocation:{getCurrentPosition(success){calls.geo++;success({coords:{latitude:46.002,longitude:15.003}});}}},L,DoggyDropBasemap:{addTo(){calls.tiles++;}},requestAnimationFrame:f=>f()};
 vm.runInNewContext(code,context);return{elements,calls,map,change(reason){elements.get('contributionReason').value=reason;elements.get('contributionReason').events.change();}};
}
test('photo form does not initialize a coordinate map or collect location',()=>{const f=fixture(true);assert.equal(f.calls.geo,0);assert.equal(f.calls.maps,0);});
test('issue form and selecting wrong-location never request geolocation automatically',()=>{const f=fixture();assert.equal(f.calls.maps,0);f.change('WRONG_LOCATION');assert.equal(f.calls.geo,0);assert.equal(f.calls.maps,1);f.change('WRONG_LOCATION');assert.equal(f.calls.maps,1);});
test('explicit locate shows proposed coordinates separately from current bin',()=>{const f=fixture();f.change('WRONG_LOCATION');f.elements.get('contributionLocate').events.click();assert.equal(f.calls.geo,1);assert.equal(f.elements.get('proposalLatitude').value,'46.002000');assert.equal(f.elements.get('locationProposal').dataset.lat,'46');});
test('map placement writes the proposed coordinate fields',()=>{const f=fixture();f.change('WRONG_LOCATION');f.map.events.click({latlng:{lat:46.1,lng:15.2}});assert.equal(f.elements.get('proposalLongitude').value,'15.200000');assert.equal(f.calls.geo,0);});
test('switching reasons displays only relevant controls',()=>{const f=fixture();f.change('DUPLICATE');assert.equal(f.elements.get('duplicateProposal').hidden,false);assert.equal(f.elements.get('locationProposal').hidden,true);f.change('OTHER');assert.equal(f.elements.get('duplicateProposal').hidden,true);});
test('explicit numeric input updates proposal with no geolocation',()=>{const f=fixture();f.change('WRONG_LOCATION');f.elements.get('proposalLatitude').value='46.12';f.elements.get('proposalLongitude').value='15.34';f.elements.get('proposalLongitude').events.change();assert.equal(f.elements.get('proposalLatitude').value,'46.120000');assert.equal(f.calls.geo,0);});

const home=fs.readFileSync(path.resolve(__dirname,'../../DoggyDrop/Views/Map/Index.cshtml'),'utf8');
const actionCode=home.slice(home.indexOf('async function sendBinAction('),home.indexOf('function findNearestTrashBin()'));
for(const action of ['used','full','useful','not-useful'])test(`actual Home ${action} request carries rendered token and unambiguous form action`,async()=>{
 const feedback={textContent:''},requests=[];
 const context={document:{getElementById:()=>feedback},URLSearchParams,requestVerificationToken:'server-rendered-test-token',bins:[],
  fetch:async(url,options)=>{requests.push({url,options});return{ok:true,status:200,redirected:false,json:async()=>({id:7})};}};
 vm.runInNewContext(actionCode,context);await context.sendBinAction(7,action);
 assert.equal(requests.length,1);const {url,options}=requests[0];assert.equal(url,'/Map/BinAction/7');assert.equal(options.method,'POST');
 assert.equal(options.body.get('__RequestVerificationToken'),'server-rendered-test-token');assert.equal(options.body.get('binAction'),action);
 assert.equal(options.body.has('action'),false);
});
