const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), test = require('node:test');
const source = fs.readFileSync(path.join(__dirname, '../../DoggyDrop/wwwroot/js/walking-route.js'), 'utf8');
const origin = {lat:46.56,lng:15.64}, destination = {lat:46.561,lng:15.641};
const good = {points:[[46.56,15.64],[46.561,15.641]],distanceMeters:245.5,durationSeconds:189};
function fixture(send) { const calls = [], window = {}; vm.runInNewContext(source, {window, fetch:async (...args)=>{calls.push(args);return send(...args);}}); return {request:window.DoggyDropWalkingRoute.request,calls}; }
test('walking route uses only same-origin protected POST and provider metrics', async()=>{
 const f=fixture(()=>({ok:true,json:async()=>good})); const r=await f.request(origin,destination,'csrf');
 assert.equal(r.distanceMeters,245.5);assert.equal(r.durationSeconds,189);assert.equal(r.isFallback,false);
 const [url,o]=f.calls[0];assert.equal(url,'/api/walking-route');assert.equal(o.method,'POST');assert.equal(o.credentials,'same-origin');assert.equal(o.cache,'no-store');assert.equal(o.headers.RequestVerificationToken,'csrf');
 assert.deepEqual(JSON.parse(o.body),{origin:{latitude:46.56,longitude:15.64},destination:{latitude:46.561,longitude:15.641}});
});
for(const status of [400,401,429,500,503]) test(`HTTP ${status} gives no walking geometry or retry`,async()=>{const f=fixture(()=>({ok:false,status}));assert.equal(await f.request(origin,destination,'csrf'),null);assert.equal(f.calls.length,1);});
test('malformed or missing metrics fail closed',async()=>{
 for(const body of [{}, {...good,points:[]}, {...good,points:[[91,15],[46,15]]}, {...good,distanceMeters:NaN}, {...good,distanceMeters:0}]){const f=fixture(()=>({ok:true,json:async()=>body}));assert.equal(await f.request(origin,destination,'csrf'),null);}
});
test('missing duration is not inferred from distance',async()=>{const f=fixture(()=>({ok:true,json:async()=>({...good,durationSeconds:null})}));assert.equal((await f.request(origin,destination,'csrf')).durationSeconds,null);});
test('invalid coordinates cause no request',async()=>{const f=fixture(()=>{throw Error('network');});assert.equal(await f.request({lat:NaN,lng:15},destination,'csrf'),null);assert.equal(f.calls.length,0);});
test('transport errors return unavailable and cancellation propagates',async()=>{
 const f=fixture(()=>{throw Error('offline');});assert.equal(await f.request(origin,destination,'csrf'),null);
 const a=fixture(()=>{const e=Error('cancelled');e.name='AbortError';throw e;});await assert.rejects(()=>a.request(origin,destination,'csrf'),{name:'AbortError'});
});
test('Home has no OSRM or car handoff and clearly labels direct fallback',()=>{
 const home=fs.readFileSync(path.join(__dirname,'../../DoggyDrop/Views/Map/Index.cshtml'),'utf8');
 assert.doesNotMatch(home,/router\.project-osrm|google\.com\/maps|distanceMeters\s*\/\s*82/);
 assert.match(home,/DoggyDropWalkingRoute.request/);assert.match(home,/zračna razdalja/);assert.match(home,/durationSeconds/);
});
test('Planner preserves short provider routes and never substitutes a generated walking loop',()=>{
 const planner=fs.readFileSync(path.join(__dirname,'../../DoggyDrop/Views/Walks/Planner.cshtml'),'utf8');
 const block=planner.slice(planner.indexOf('            const previewRoutePoints ='),planner.indexOf('            if (debugRouteEnabled) setPlannerDebug("Preview"'));
 for(const length of [0,1,2,3,8]) { const points=Array.from({length},(_,i)=>({lat:46+i/100,lng:15}));let generated=0;
 const result=vm.runInNewContext(block+';previewRoutePoints',{routePoints:points,plannerIsWalkingRoute:true,center:points[0],stopPoints:[],readTargetDistanceKm:()=>3,buildPreviewLoop:()=>{generated++;return [];}});
 assert.equal(generated,0);assert.equal(result.length,length>=2?length:0);if(length>=2)assert.equal(result,points);
 }
});
// Execute Home's actual lifecycle functions with controlled promises, not timers or network.
const homeSource = fs.readFileSync(path.join(__dirname, '../../DoggyDrop/Views/Map/Index.cshtml'), 'utf8');
function homeSection(start, end) {
    const first = homeSource.indexOf(start), last = homeSource.indexOf(end, first + start.length);
    assert.ok(first >= 0 && last > first, `Home lifecycle section missing: ${start}`);
    return homeSource.slice(first, last);
}
const navigationInitialization = homeSection('        const navigation = {', '        const navigationArrivalMeters');
const navigationFunctions = homeSection('        function stopInAppNavigation()', '        function updateDirectionsPanelForNavigation()');
const movementGate = homeSection('                    if (!navigation.arrived && !navigation.routePending', '                    updateDirectionsPanelForNavigation();');
function navigationFixture() {
    const requests = [], layers = [], clearedWatches = [];
    const panel = { style: {}, innerHTML: '', hidden: false };
    const context = {
        AbortController, clearTimeout, requestVerificationToken: 'test-csrf',
        setBinNavigationMode() {}, setDirectionsPanelCompact() {}, homeActiveWalkId: 0,
        navigator: { geolocation: { clearWatch(id) { clearedWatches.push(id); } } },
        document: { getElementById() { return panel; } }, map: {},
        updateDirectionsPanelForNavigation() {}, getDistanceMeters: () => 150,
        DoggyDropWalkingRoute: { request(origin, destination, token, signal) {
            return new Promise((resolve, reject) => requests.push({ origin, destination, token, signal, resolve, reject }));
        } },
        L: { polyline(points, options) {
            const layer = { points, options, addTo() { layers.push(layer); return layer; },
                bringToFront() {}, remove() { layer.removed = true; } };
            return layer;
        } }
    };
    vm.createContext(context);
    vm.runInContext(navigationInitialization + navigationFunctions + ';globalThis.navigationState = navigation;', context);
    const pendingRequests = [];
    const requestNavigationRoute = context.requestNavigationRoute;
    context.requestNavigationRoute = (...args) => {
        const pending = requestNavigationRoute(...args);
        pendingRequests.push(pending);
        return pending;
    };
    const state = context.navigationState;
    Object.assign(state, { active: true, position: { ...origin }, destination: { latitude: destination.lat, longitude: destination.lng } });
    return { context, state, requests, layers, panel, clearedWatches, pendingRequests,
        request: () => context.requestNavigationRoute(false), stop: () => context.stopInAppNavigation(),
        movement(distance) { context.userLocation = state.position; context.getDistanceMeters = () => distance; vm.runInContext(movementGate, context); } };
}
const routed = distanceMeters => ({ ...good, distanceMeters, isFallback: false });

test('Home newer request aborts previous and old cancellation leaves newer request pending', async () => {
    const f = navigationFixture();
    const oldRequest = f.request(), newRequest = f.request();
    assert.equal(f.requests[0].signal.aborted, true);
    f.requests[0].reject(Object.assign(new Error('cancelled'), { name: 'AbortError' }));
    await oldRequest;
    assert.equal(f.state.routePending, true);
    assert.equal(f.layers.length, 0);
    f.requests[1].resolve(routed(200)); await newRequest;
    assert.equal(f.state.routePending, false);
    assert.equal(f.state.distanceMeters, 200);
});

test('Home newer success wins over late older success or failure', async () => {
    for (const oldResult of [routed(999), null]) {
        const f = navigationFixture();
        const oldRequest = f.request();
        f.state.position = { lat: 46.5608, lng: 15.6408 };
        const newRequest = f.request();
        f.requests[1].resolve(routed(200)); await newRequest;
        const winningLayer = f.state.routeLayer;
        f.requests[0].resolve(oldResult); await oldRequest;
        assert.equal(f.state.distanceMeters, 200);
        assert.equal(f.state.durationSeconds, 189);
        assert.equal(f.state.routeStatus, 'routed');
        assert.equal(f.state.routeLayer, winningLayer);
        assert.equal(f.layers.length, 1);
        assert.equal(f.state.lastSuccessfulPosition.lat, 46.5608);
    }
});

test('Home stop cancels pending navigation and late success or failure cannot revive it', async () => {
    for (const result of [routed(777), null]) {
        const f = navigationFixture();
        f.state.watchId = 7;
        const pending = f.request(); f.stop();
        assert.equal(f.requests[0].signal.aborted, true);
        assert.deepEqual(f.clearedWatches, [7]);
        f.requests[0].resolve(result); await pending;
        assert.equal(f.state.active, false);
        assert.equal(f.state.routeLayer, null);
        assert.equal(f.state.routePending, false);
        assert.equal(f.state.distanceMeters, null);
        assert.equal(f.state.durationSeconds, null);
        assert.equal(f.layers.length, 0);
        assert.equal(f.panel.style.display, 'none');
    }
});

test('Home unavailable route clears routed metrics and retry anchor and draws only approximation', async () => {
    const f = navigationFixture();
    let pending = f.request(); f.requests[0].resolve(routed(200)); await pending;
    const oldLayer = f.state.routeLayer;
    pending = f.request(); f.requests[1].resolve(null); await pending;
    assert.equal(oldLayer.removed, true);
    assert.equal(f.state.routeStatus, 'approximate');
    assert.equal(f.state.distanceMeters, 150);
    assert.equal(f.state.durationSeconds, null);
    assert.equal(f.state.routePending, false);
    assert.equal(f.state.lastSuccessfulPosition, null);
    assert.equal(f.state.retryTimer, null);
    assert.equal(f.state.routeLayer.options.dashArray, '7 9');
    assert.deepEqual(JSON.parse(JSON.stringify(f.state.routeLayer.points)), [[origin.lat, origin.lng], [destination.lat, destination.lng]]);
    f.movement(100); assert.equal(f.requests.length, 2);
});

test('Home movement uses the completed request origin and recalculates at the actual 60m threshold', async () => {
    const f = navigationFixture();
    const pending = f.request();
    f.state.position = { lat: 46.5608, lng: 15.6408 };
    f.movement(100); assert.equal(f.requests.length, 1);
    f.requests[0].resolve(routed(200)); await pending;
    assert.equal(f.state.lastSuccessfulPosition.lat, origin.lat);
    f.movement(59); assert.equal(f.requests.length, 1);
    f.movement(60); assert.equal(f.requests.length, 2);
    assert.equal(f.requests[1].origin.lat, f.state.position.lat);
    assert.equal(f.state.routePending, true);
    f.movement(100); assert.equal(f.requests.length, 2);
    f.requests[1].resolve(routed(180));
    await f.pendingRequests[1];
    assert.equal(f.state.routePending, false);
    assert.equal(f.state.lastSuccessfulPosition.lat, f.state.position.lat);
});

test('Home pending, approximate and arrived states suppress movement recalculation', () => {
    for (const overrides of [{ routePending: true }, { routeStatus: 'approximate' }, { arrived: true }, { lastSuccessfulPosition: null }]) {
        const f = navigationFixture();
        Object.assign(f.state, { routeStatus: 'routed', lastSuccessfulPosition: { ...origin } }, overrides);
        f.movement(100);
        assert.equal(f.requests.length, 0);
    }
});
