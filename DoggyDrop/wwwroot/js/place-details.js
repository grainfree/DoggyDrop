(() => {
    const image = document.querySelector(".places-details__media img");
    const fallback = document.querySelector(".places-details__media-fallback");
    if (image && fallback) {
        const showFallback = () => {
            image.hidden = true;
            fallback.hidden = false;
        };
        image.addEventListener("error", showFallback, { once: true });
        if (image.complete && image.naturalWidth === 0) showFallback();
    }

    const element = document.getElementById("placeDetailsMap");
    if (!element || !window.L) return;
    const lat = Number(element.dataset.latitude);
    const lng = Number(element.dataset.longitude);
    if (!Number.isFinite(lat) || !Number.isFinite(lng) || Math.abs(lat) > 90 || Math.abs(lng) > 180) return;

    const map = L.map(element, { scrollWheelZoom: false }).setView([lat, lng], 16);
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 19,
        attribution: "&copy; OpenStreetMap"
    }).addTo(map);
    L.circleMarker([lat, lng], {
        radius: 10, color: "#fff", weight: 3, fillColor: "#16805d", fillOpacity: 1
    }).addTo(map);
    requestAnimationFrame(() => map.invalidateSize());
})();
