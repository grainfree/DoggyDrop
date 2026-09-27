const test = require("node:test"), assert = require("node:assert/strict");
const fs = require("node:fs"), path = require("node:path"), vm = require("node:vm");
const root = path.resolve(__dirname, "../..");
const read = file => fs.readFileSync(path.join(root, file), "utf8");
const window = { L: { divIcon: options => options } };
vm.runInNewContext(read("DoggyDrop/wwwroot/js/place-marker.js"), { window, URL });
vm.runInNewContext(read("DoggyDrop/wwwroot/js/place-discovery.js"), { window, document: { getElementById: () => null } });
const item = { isCommercial: true, categoryKey: "pet-shop", iconClass: "bi-bag-fill", isCurrentlyFeatured: true };
test("current commercial markers get a subtle class without changing marker size", () => {
 const featured = window.DoggyDropPlaceMarker.createIcon(item), normal = window.DoggyDropPlaceMarker.createIcon({...item,isCurrentlyFeatured:false});
 assert.match(featured.html,/managed-place-pin--featured/);assert.doesNotMatch(normal.html,/managed-place-pin--featured/);
 assert.deepEqual(featured.iconSize,normal.iconSize);assert.deepEqual(featured.iconAnchor,normal.iconAnchor);
});
test("public destinations and future or expired markers do not get Featured treatment", () => {
 for(const place of [{...item,isCommercial:false,categoryKey:"dog-park"},{...item,isCommercial:false,categoryKey:"dog-beach"},{...item,isCurrentlyFeatured:false}])
  assert.doesNotMatch(window.DoggyDropPlaceMarker.createIcon(place).html,/managed-place-pin--featured/);
});
test("selected state wins over Featured halo and Home focus priority is retained", () => {
 assert.match(window.DoggyDropPlaceMarker.createIcon(item,{selected:true}).html,/managed-place-pin--selected/);
 assert.match(read("DoggyDrop/wwwroot/css/places.css"),/managed-place-pin--featured:not\(\.managed-place-pin--selected\)/);
 const home=read("DoggyDrop/Views/Map/Index.cshtml");assert.match(home,/marker.setZIndexOffset\(500\)/);
 assert.match(home,/place.isCurrentlyFeatured === true && place.isCommercial === true/);
});
test("distance sorting outranks server Featured-first order even across 100 kilometres", () => {
 const items=[{order:0,category:"1",latitude:47,longitude:15,isCurrentlyFeatured:true},{order:1,category:"1",latitude:46.001,longitude:15},{order:2,category:"2",latitude:46.002,longitude:15}];
 const visible=window.DoggyDropPlaceDiscovery.visibleItems;
 assert.deepEqual(Array.from(visible(items,"all",null),p=>p.order),[0,1,2]);
 assert.deepEqual(Array.from(visible(items,"all",{latitude:46,longitude:15,accuracy:10}),p=>p.order),[1,2,0]);
 assert.deepEqual(Array.from(visible(items,"1",null),p=>p.order),[0,1]);
 assert.deepEqual(Array.from(visible(items,"1",{latitude:46,longitude:15,accuracy:10}),p=>p.order),[1,0]);
});
test("Admin category change disables and clears promotion without changing amenities", () => {
 const handlers={}, elements={Category:{selectedOptions:[{dataset:{featuredEligible:"true"}}],addEventListener:(name,fn)=>handlers[name]=fn},placeFeaturedFields:{},IsFeatured:{checked:true},FeaturedFromLocal:{value:"2026-10-01T12:00"},FeaturedUntilLocal:{value:"2026-11-01T12:00"}};
 vm.runInNewContext(read("DoggyDrop/wwwroot/js/place-editor.js"),{window:{addEventListener:()=>{}},document:{getElementById:id=>elements[id]??null,querySelector:()=>null}});
 assert.equal(elements.placeFeaturedFields.disabled,false);elements.Category.selectedOptions[0].dataset.featuredEligible="false";handlers.change();
 assert.equal(elements.placeFeaturedFields.disabled,true);assert.equal(elements.IsFeatured.checked,false);assert.equal(elements.FeaturedFromLocal.value,"");assert.equal(elements.FeaturedUntilLocal.value,"");
 elements.Category.selectedOptions[0].dataset.featuredEligible="true";handlers.change();assert.equal(elements.placeFeaturedFields.disabled,false);assert.equal(elements.IsFeatured.checked,false);
});
