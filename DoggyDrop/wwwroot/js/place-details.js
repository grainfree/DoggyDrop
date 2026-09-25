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
    if (!element || !window.L || !window.DoggyDropBasemap) return;
    const lat = Number(element.dataset.latitude);
    const lng = Number(element.dataset.longitude);
    if (!Number.isFinite(lat) || !Number.isFinite(lng) || Math.abs(lat) > 90 || Math.abs(lng) > 180) return;

    const map = L.map(element, { scrollWheelZoom: false }).setView([lat, lng], 16);
    window.DoggyDropBasemap.addTo(map, element.dataset.cartoBasemapKey);
    const veterinarian = element.dataset.category === "1";
    const icon = L.divIcon({
        className: "", iconSize: [38, 38], iconAnchor: [19, 19],
        html: `<span class="managed-place-pin managed-place-pin--${veterinarian ? "veterinarian" : "pet-shop"}" role="img" aria-label="${veterinarian ? "Veterinar" : "Trgovina za male živali"}"><i class="bi ${veterinarian ? "bi-heart-pulse-fill" : "bi-bag-fill"}" aria-hidden="true"></i></span>`
    });
    L.marker([lat, lng], { icon }).addTo(map);
    const refresh = () => map.invalidateSize({ pan: false });
    requestAnimationFrame(refresh);
    if (window.ResizeObserver) new window.ResizeObserver(refresh).observe(element);
    else window.addEventListener("resize", () => requestAnimationFrame(refresh));
})();
