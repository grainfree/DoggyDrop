(() => {
    "use strict";
    const valid = p => p && Number.isSafeInteger(p.id) && p.id > 0 && Number.isFinite(p.latitude) &&
        Number.isFinite(p.longitude) && Math.abs(p.latitude) <= 90 && Math.abs(p.longitude) <= 180;
    const label = p => typeof p.name === "string" && p.name.trim() ? p.name : "Pitnik";
    const svg = '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M12 2C10 6 5 11 5 15a7 7 0 0 0 14 0c0-4-5-9-7-13Z" fill="currentColor"/><path d="M8 15a4 4 0 0 0 4 4" fill="none" stroke="white" stroke-width="1.5" stroke-linecap="round"/></svg>';
    function icon() { return L.divIcon({className:"waterpoint-marker",html:'<span class="waterpoint-pin">'+svg+'</span>',iconSize:[34,40],iconAnchor:[17,38],popupAnchor:[0,-34]}); }
    function popup(point,navigate) {
        const node=document.createElement("section");node.className="waterpoint-popup";
        function text(tag,value){const e=document.createElement(tag);e.textContent=value;node.append(e);return e;}
        text("h3",label(point));text("p","Vir označuje pitnik. Kakovost vode ni neodvisno preverjena.");
        text("p",point.seasonality===1?"Po viru sezonsko; preveri lokalno obvestilo.":point.seasonality===2?"Vir navaja celoletno uporabo; upoštevaj lokalna obvestila.":"Sezonskost ni znana.");
        if(point.access===0)text("p","Dostop ni posebej naveden v viru; upoštevaj oznake na kraju.");
        if(point.dogAccess===1)text("p","Vir navaja dovoljen dostop psov; posoda za pse ni potrjena.");
        if(point.dogAccess===2)text("p","Vir navaja, da psi niso dovoljeni.");
        const button=text("button","Peš do pitnika");button.type="button";button.addEventListener("click",()=>navigate(point.id));
        const trust=document.createElement("div");trust.className="infrastructure-confirmation";trust.dataset.confirmKind="water";trust.dataset.confirmId=String(point.id);node.append(trust);
        window.DoggyDropConfirmations?.render(trust,point.trust);
        if(point.sourceName){const source=text("p","Vir: ");let url;
            try {url=new URL(point.sourceUrl);if(!["https:","http:"].includes(url.protocol)||url.username||url.password)url=null;}catch{url=null;}
            const e=document.createElement(url?"a":"span");e.textContent=point.sourceName;if(url){e.href=url.href;e.target="_blank";e.rel="noopener noreferrer";}source.append(e);}
        return node;
    }
    function distance(a,b) {const rad=Math.PI/180;const x=(b.latitude-a.lat)*rad,y=(b.longitude-a.lng)*rad;
        const h=Math.sin(x/2)**2+Math.cos(a.lat*rad)*Math.cos(b.latitude*rad)*Math.sin(y/2)**2;return 6371000*2*Math.asin(Math.min(1,Math.sqrt(h)));}
    function nearest(points,origin,maxMetres=25000) {
        if(!origin||!Number.isFinite(origin.lat)||!Number.isFinite(origin.lng)||Math.abs(origin.lat)>90||Math.abs(origin.lng)>180)return null;
        return points.filter(valid).map(point=>({point,metres:distance(origin,point)})).filter(p=>p.metres<=maxMetres)
            .sort((a,b)=>a.metres-b.metres||a.point.id-b.point.id)[0]||null;
    }
    function populate(layer,points,navigate) {
        layer.clearLayers();const markers=new Map();
        points.filter(valid).forEach(p=>{if(markers.has(p.id))return;
            const marker=L.marker([p.latitude,p.longitude],{icon:icon(),title:label(p),alt:"Pitnik: "+label(p),pane:"waterMarkers"})
                .bindPopup(()=>popup(p,navigate),{maxWidth:280,
                    maxHeight:Math.max(120,Math.min(320,(Number(window.innerHeight)||900)-400)),
                    autoPanPaddingTopLeft:[16,90],autoPanPaddingBottomRight:[16,200]}).addTo(layer);markers.set(p.id,marker);});return markers;
    }
    async function current(id,signal) {
        const response=await fetch(id?`/api/waterpoints/${id}`:"/api/waterpoints",{cache:"no-store",credentials:"same-origin",signal});
        if(!response.ok)throw new Error("Pitnik trenutno ni na voljo. Osveži zemljevid.");return response.json();
    }
    async function route(origin,id,token,signal) {
        const response=await fetch(`/api/waterpoints/${id}/route`,{method:"POST",credentials:"same-origin",cache:"no-store",signal,
            headers:{"Content-Type":"application/json","RequestVerificationToken":token},body:JSON.stringify({origin:{latitude:origin.lat,longitude:origin.lng}})});
        if(response.status===410){const error=new Error("Pitnik ni več na voljo za navigacijo.");error.unavailable=true;throw error;}
        if(!response.ok)return null;
        const value=await response.json();
        if(!Array.isArray(value.points)||value.points.length<2||!value.points.every(p=>Array.isArray(p)&&p.length===2&&p.every(Number.isFinite)&&Math.abs(p[0])<=90&&Math.abs(p[1])<=180)||!Number.isFinite(value.distanceMeters)||value.distanceMeters<=0)return null;
        return {points:value.points,distanceMeters:value.distanceMeters,durationSeconds:Number.isFinite(value.durationSeconds)&&value.durationSeconds>0?value.durationSeconds:null,isFallback:false};
    }
    window.DoggyDropWaterPoints={valid,label,icon,popup,nearest,populate,current,route};
})();
