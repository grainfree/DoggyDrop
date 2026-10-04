const test = require('node:test'), assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'DoggyDrop/wwwroot/js/home-map-locations.js'), 'utf8');
function fixture() {
    class Layer {
        addTo(map) { map.addLayer(this); return this; }
        remove() { this._map?.removeLayer(this); return this; }
        static extend(methods) { class Sub extends this { constructor(...a) { super(); this.initialize?.(...a); } } Object.assign(Sub.prototype, methods); return Sub; }
    }
    const container = {classList:{toggle(){}},addEventListener(){},removeEventListener(){}};
    const map = {layers:new Set(),on(){},getContainer:()=>container,getZoom:()=>10,setView(){},createPane:()=>({style:{}}),
        addLayer(l){if(!this.layers.has(l)){this.layers.add(l);l._map=this;l.onAdd?.(this);}},
        removeLayer(l){if(this.layers.delete(l)){l.onRemove?.(this);l._map=null;}}};
    function group() { return Object.assign(new Layer(), {members:new Set(),addLayer(m){m.options??={pane:'markerPane'};this.members.add(m);return this;},removeLayer(m){this.members.delete(m);return this;},
        addLayers(a){a.forEach(m=>this.addLayer(m));},removeLayers(a){a.forEach(m=>this.members.delete(m));}}); }
    const cluster=group(), focus=group(), pending=[];
    cluster.zoomToShowLayer=(marker,done)=>pending.push({marker,done});
    const L={Layer,DivIcon:Layer,layerGroup:()=>focus,markerClusterGroup:options=>{cluster.options=options;return cluster;}};
    const window={L};vm.runInNewContext(source,{window,document:{createElement:()=>({setAttribute(){}})},Map,Set});
    const locations=window.DoggyDropHomeLocations.create(map);
    const reveal=locations.reveal;locations.reveal=(marker,callback)=>{if(marker)marker.getLatLng=()=>({lat:46,lng:15});return reveal(marker,callback);};
    const bins=locations.createLayer(),places=locations.createLayer();
    return {locations,bins,places,map,cluster,focus,pending};
}
test('one combined group counts only enabled logical layers without repeated-add duplicates',()=>{
    const f=fixture(),b={},p={};f.bins.addLayer(b).addLayer(b);f.places.addLayer(p);
    f.bins.addTo(f.map);f.places.addTo(f.map);f.bins.addTo(f.map);
    assert.equal(f.cluster.members.size,2);f.bins.remove();assert.deepEqual([...f.cluster.members],[p]);
    f.bins.addTo(f.map);assert.equal(f.cluster.members.size,2);f.places.remove();assert.deepEqual([...f.cluster.members],[b]);
});
test('refresh/category subsets replace rather than append; hidden refresh does not leak members',()=>{
    const f=fixture(),a={},b={},c={};f.places.setLayers([a,b]).addTo(f.map);
    f.places.setLayers([b,c,c]);assert.deepEqual([...f.cluster.members],[b,c]);
    f.places.remove();f.places.setLayers([a]);assert.equal(f.cluster.members.size,0);
    f.places.addTo(f.map);assert.deepEqual([...f.cluster.members],[a]);
    f.places.clearLayers();assert.equal(f.cluster.members.size,0);
});
test('focus reveals synchronously and pins exactly one member; closing restores it without loss or duplicates',()=>{
    const f=fixture(),a={},b={};f.bins.setLayers([a,b]).addTo(f.map);let called=0;
    f.locations.reveal(a,()=>called++);assert.equal(f.pending.length,0);
    assert.equal(called,1);assert.deepEqual([...f.focus.members],[a]);assert.deepEqual([...f.cluster.members],[b]);
    assert.equal(a.options.pane,'focusedLocations');
    f.locations.release(a);assert.equal(f.focus.members.size,0);assert.equal(f.cluster.members.size,2);
    assert.equal(a.options.pane,'markerPane');
});
test('newer focus wins without scheduling any late zoom callback',()=>{
    const f=fixture(),a={},b={};f.places.setLayers([a,b]).addTo(f.map);const seen=[];
    f.locations.reveal(a,()=>seen.push(a));f.locations.reveal(b,()=>seen.push(b));
    assert.equal(f.pending.length,0);assert.deepEqual(seen,[a,b]);assert.deepEqual([...f.focus.members],[b]);
});
test('filter/navigation removal rejects hidden reveals and removes pinned targets',()=>{
    const f=fixture(),a={};f.bins.addLayer(a).addTo(f.map);let called=0;
    f.bins.remove();f.locations.reveal(a,()=>called++);assert.equal(called,0);
    f.bins.addTo(f.map);f.locations.pin(a);f.bins.remove();
    assert.equal(f.focus.members.size,0);assert.equal(f.cluster.members.size,0);
});
test('refresh deleting selected target clears focus and stale callbacks',()=>{
    const f=fixture(),a={},b={};f.places.addLayer(a).addTo(f.map);f.locations.pin(a);
    f.places.setLayers([b]);assert.equal(f.focus.members.size,0);assert.deepEqual([...f.cluster.members],[b]);
});
test('a marker cannot belong to two logical layers',()=>{
    const f=fixture(),a={};f.bins.addLayer(a);
    assert.throws(()=>f.places.addLayer(a),/already belongs/);assert.throws(()=>f.places.setLayers([a]),/already belongs/);
});
test('Home-only assets leave precision maps, Active and Planner free of clustering',()=>{
    const home=fs.readFileSync(path.join(root,'DoggyDrop/Views/Map/Index.cshtml'),'utf8');
    assert.match(home,/leaflet@1\.9\.4/);assert.match(home,/leaflet\.markercluster-1\.5\.3/);
    assert.match(home,/if \(homeActiveWalkId > 0 && !linkedBin\) focusMapNearUser\(Boolean\(linkedManagedPlace\)\)/);
    for(const file of ['Views/Map/Add.cshtml','Views/Places/Details.cshtml','Views/Walks/Active.cshtml','Views/Walks/Planner.cshtml','Views/AdminPlaces/Create.cshtml','Views/AdminPlaces/Edit.cshtml']) {
        const content=fs.readFileSync(path.join(root,'DoggyDrop',file),'utf8');assert.doesNotMatch(content,/home-map-locations|markercluster/i,file);
    }
});
for(const preserveFocus of [true,false])test(`startup geolocation preserves location updates with explicit focus=${preserveFocus}`,()=>{
    const home=fs.readFileSync(path.join(root,'DoggyDrop/Views/Map/Index.cshtml'),'utf8');
    const body=home.match(/        function focusMapNearUser\([^\n]*\) \{[\s\S]*?\n        \}/)[0];
    let success,fail,views=0,fits=0,updates=0;
    const c=vm.createContext({navigator:{geolocation:{getCurrentPosition:(ok,bad)=>{success=ok;fail=bad;}}},window:{isSecureContext:true},
        userLocation:null,userMarker:{setLatLng:()=>updates++},homeActiveWalkId:0,bins:[{latitude:46,longitude:15}],
        map:{setView:()=>views++,fitBounds:()=>fits++},L:{latLngBounds:()=>({pad:()=>({})})},
        updateFounderPrompt(){},loadNearbyDogs(){},renderNearbySuggestions(){}});
    vm.runInContext(body+';focusMapNearUser('+preserveFocus+');',c);
    success({coords:{latitude:46,longitude:15}});fail();
    assert.equal(updates,1);assert.equal(c.userLocation.lat,46);assert.equal(c.userLocation.lng,15);
    assert.equal(views,preserveFocus?0:1);assert.equal(fits,preserveFocus?0:1);
});
