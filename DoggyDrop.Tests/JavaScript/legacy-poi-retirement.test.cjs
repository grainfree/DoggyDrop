const test = require('node:test'), assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../DoggyDrop/Views/Map/Index.cshtml'), 'utf8');
function fn(name) {
    const start = source.indexOf(`        function ${name}(`);
    assert.ok(start > 0, name);
    const end = source.indexOf('\n        }', start) + '\n        }'.length;
    return source.slice(start, end);
}
const names = ['getExploreItems', 'renderExploreList', 'renderNearbySuggestions', 'buildNearbySuggestionCard',
    'buildCommunityBinCard', 'getNearbyFallbackArea', 'focusExploreItem', 'ensureLayerVisible', 'exploreItemIcon',
    'normalizeSearch', 'escapeHtml', 'escapeAttribute', 'getDistanceMeters', 'toRadians', 'formatDistance'];
function fixture() {
    const elements = Object.fromEntries(['exploreList','exploreSearch','nearbySuggestionsList','showPlaces','showBins'].map(id => [id,{value:'',innerHTML:'',checked:false}]));
    const categoryKeys = ['dog-park','dog-friendly-cafe','dog-beach','pet-shop','veterinarian','groomer','dog-school'];
    const calls = [];
    const context = vm.createContext({
        document:{getElementById:id=>elements[id]},
        bins:[{id:9,name:'Approved bin',latitude:46,longitude:15}],
        managedPlaces:categoryKeys.map((categoryKey,i)=>({id:i+1,name:`Current ${categoryKey}`,categoryKey,categoryLabel:categoryKey,
            iconClass:`dd-place-icon--${categoryKey}`,latitude:46,longitude:15})),
        map:{getCenter:()=>({lat:46,lng:15}),getZoom:()=>13,setView:()=>calls.push('view')},
        userLocation:{lat:46,lng:15}, nearbyFallbackAreas:[{name:'Fixture',latitude:46,longitude:15}],
        exploreFilter:'all', DoggyDropPlaceMarker:{iconClass:p=>/^dd-place-icon--[a-z-]+$/.test(p.iconClass)?p.iconClass:'dd-place-icon--other'},
        placeMarkers:new Map([["place-1",{openPopup:()=>calls.push('place-popup')}]]),
        binMarkers:new Map([[9,{openPopup:()=>calls.push('bin-popup')}]]),
        managedPlaceLayer:{addTo:()=>calls.push('places')},binLayer:{addTo:()=>calls.push('bins')},
        homeLocations:{reveal:(marker,callback)=>{if(marker)callback();}},
        openBinDetail:()=>calls.push('bin-detail'),
        saveMapSetting:(key,value)=>calls.push([key,value])
    });
    vm.runInContext(names.map(fn).join('\n'),context);
    return {context,elements,calls};
}
test('Explore contains exactly current managed categories and bins, including dog park and cafe',()=>{
    const {context:c}=fixture(); const rows=c.getExploreItems();
    assert.equal(rows.length,8); assert.equal(rows.filter(r=>r.type==='bin').length,1);
    assert.equal(rows.filter(r=>r.type==='dog-park').length,1); assert.equal(rows.filter(r=>r.type==='dog-friendly-cafe').length,1);
    assert.ok(rows.every(r=>r.key.startsWith('place-')||r.key==='bin-9'));
    assert.ok(rows.every(r=>!['park','water','cafe'].includes(r.type)));
});
test('Explore filter/search/empty states cannot recreate retired entries',()=>{
    const {context:c,elements:e}=fixture(); c.exploreFilter='dog-park';c.renderExploreList();
    assert.match(e.exploreList.innerHTML,/Current dog-park/);assert.doesNotMatch(e.exploreList.innerHTML,/Current dog-friendly-cafe/);
    c.exploreFilter='dog-friendly-cafe'; c.renderExploreList();assert.match(e.exploreList.innerHTML,/Current dog-friendly-cafe/);
    e.exploreSearch.value='Pitnik Lent';c.renderExploreList();assert.match(e.exploreList.innerHTML,/Ni zadetkov/);
    c.managedPlaces=[];c.bins=[];c.exploreFilter='all';e.exploreSearch.value='';c.renderExploreList();assert.match(e.exploreList.innerHTML,/Ni zadetkov/);
});
test('Nearby suggestions resolve only persisted park/cafe/bin targets and remain safe when empty',()=>{
    const {context:c,elements:e}=fixture();c.renderNearbySuggestions();
    assert.match(e.nearbySuggestionsList.innerHTML,/place-1/);assert.match(e.nearbySuggestionsList.innerHTML,/dog-friendly-cafe/);
    assert.doesNotMatch(e.nearbySuggestionsList.innerHTML,/water|park-46|Dog friendly Lent/);
    c.managedPlaces=[];c.bins=[];c.renderNearbySuggestions();assert.doesNotMatch(e.nearbySuggestionsList.innerHTML,/focusExploreItem/);
    assert.match(e.nearbySuggestionsList.innerHTML,/\/Map\/Add/);
});
test('Place and bin focus restores only the corresponding current layer; stale legacy keys do nothing',()=>{
    const {context:c,elements:e,calls}=fixture(); c.focusExploreItem('park-46-15');assert.deepEqual(calls,[]);
    c.focusExploreItem('place-1');assert.ok(e.showPlaces.checked);assert.ok(calls.includes('place-popup'));
    c.focusExploreItem('bin-9');assert.ok(e.showBins.checked);assert.ok(calls.includes('bin-detail'));
});
test('Current Place text and category icons remain escaped in discovery markup',()=>{
    const {context:c,elements:e}=fixture();c.managedPlaces[0].name='<img onerror=alert(1)>';c.managedPlaces[0].iconClass='bad" onclick="evil';
    c.renderExploreList();assert.match(e.exploreList.innerHTML,/&lt;img/);assert.doesNotMatch(e.exploreList.innerHTML,/<img|onclick="evil/);
});
test('Home has no legacy public layer/filter/check-in entry point',()=>{
    assert.doesNotMatch(source,/placeGroups|buildPlaceLayer|createPlaceIcon|parkLocationsJson|showParks|showCafes|showWater|\/Map\/ParkVisit/);
    assert.match(source,/wireLayerToggle\("showPlaces", managedPlaceLayer\)/);
});
