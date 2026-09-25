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
    const logo = document.querySelector(".places-details__logo img");
    logo?.addEventListener("error", () => { logo.parentElement.hidden = true; }, { once: true });

    const element = document.getElementById("placeDetailsMap");
    if (!element || !window.L || !window.DoggyDropBasemap || !window.DoggyDropPlaceMarker) return;
    const lat = Number(element.dataset.latitude);
    const lng = Number(element.dataset.longitude);
    if (!Number.isFinite(lat) || !Number.isFinite(lng) || Math.abs(lat) > 90 || Math.abs(lng) > 180) return;

    const map = L.map(element, { scrollWheelZoom: false }).setView([lat, lng], 16);
    window.DoggyDropBasemap.addTo(map, element.dataset.cartoBasemapKey);
    const icon = window.DoggyDropPlaceMarker.createIcon({
        category: element.dataset.category,
        logoUrl: element.dataset.logoUrl
    });
    const marker = L.marker([lat, lng], { icon });
    marker.addTo(map);
    window.DoggyDropPlaceMarker.attachImage(marker.getElement());
    const refresh = () => map.invalidateSize({ pan: false });
    requestAnimationFrame(refresh);
    if (window.ResizeObserver) new window.ResizeObserver(refresh).observe(element);
    else window.addEventListener("resize", () => requestAnimationFrame(refresh));
})();
