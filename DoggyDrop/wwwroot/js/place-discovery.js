(() => {
    function validLocation(position) {
        return position && Number.isFinite(position.latitude) && Math.abs(position.latitude) <= 90 &&
            Number.isFinite(position.longitude) && Math.abs(position.longitude) <= 180 &&
            Number.isFinite(position.accuracy) && position.accuracy >= 0 && position.accuracy <= 2000;
    }

    function distanceMeters(from, to) {
        if (!validLocation(from) || !Number.isFinite(to.latitude) || Math.abs(to.latitude) > 90 ||
            !Number.isFinite(to.longitude) || Math.abs(to.longitude) > 180) return null;
        const radians = value => value * Math.PI / 180;
        const deltaLat = radians(to.latitude - from.latitude);
        const deltaLng = radians(to.longitude - from.longitude);
        const arc = Math.sin(deltaLat / 2) ** 2 +
            Math.cos(radians(from.latitude)) * Math.cos(radians(to.latitude)) * Math.sin(deltaLng / 2) ** 2;
        return 6371000 * 2 * Math.atan2(Math.sqrt(arc), Math.sqrt(Math.max(0, 1 - arc)));
    }

    function formatDistance(meters) {
        if (!Number.isFinite(meters) || meters < 0) return "";
        if (meters > 0 && meters < 1) return "< 1 m";
        return meters < 1000 ? `${Math.round(meters)} m` :
            `${new Intl.NumberFormat("sl-SI", { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(meters / 1000)} km`;
    }

    function visibleItems(items, category, location) {
        return items.filter(item => category === "all" || String(item.category) === category)
            .map(item => ({ ...item, distance: location ? distanceMeters(location, item) : null }))
            .sort((left, right) => location
                ? (left.distance ?? Infinity) - (right.distance ?? Infinity) || left.order - right.order
                : left.order - right.order);
    }

    window.DoggyDropPlaceDiscovery = { validLocation, distanceMeters, formatDistance, visibleItems };
    const root = document.getElementById("placeDiscovery");
    const list = document.getElementById("discoveryList");
    if (!root || !list) return;

    const cards = Array.from(list.querySelectorAll(".place-discovery-card"));
    const items = cards.map((card, order) => ({
        card, order, category: card.dataset.category,
        latitude: Number(card.dataset.latitude), longitude: Number(card.dataset.longitude)
    }));
    const filters = Array.from(root.querySelectorAll(".place-discovery__filter"));
    const locate = document.getElementById("discoveryLocate");
    const status = document.getElementById("discoveryStatus");
    let category = "all";
    let location = null;

    root.querySelectorAll(".place-discovery-card__logo img").forEach(image => {
        image.addEventListener("error", () => { image.hidden = true; }, { once: true });
        if (image.complete && image.naturalWidth === 0) image.hidden = true;
    });

    function render() {
        const shown = visibleItems(items, category, location);
        cards.forEach(card => { card.hidden = true; });
        shown.forEach(item => {
            item.card.hidden = false;
            const distance = item.card.querySelector(".place-discovery-card__distance");
            distance.hidden = item.distance === null;
            distance.textContent = item.distance === null ? "" :
                `${location.accuracy > 100 ? "≈" : ""}${formatDistance(item.distance)}`;
            list.appendChild(item.card);
        });
        document.getElementById("discoveryCategoryEmpty").hidden = shown.length !== 0;
        filters.forEach(button => { button.setAttribute("aria-pressed", String(button.dataset.category === category)); });
    }

    filters.forEach(button => button.addEventListener("click", () => {
        category = button.dataset.category;
        render();
    }));
    locate.addEventListener("click", () => {
        if (!navigator.geolocation) {
            status.textContent = "Lokacija ni na voljo. Lokacije lahko še vedno pregleduješ.";
            status.hidden = false;
            return;
        }
        locate.disabled = true;
        status.textContent = "Pridobivamo lokacijo …";
        status.hidden = false;
        navigator.geolocation.getCurrentPosition(position => {
            locate.disabled = false;
            const candidate = position.coords;
            if (!validLocation(candidate)) {
                location = null;
                status.textContent = "Lokacija ni dovolj natančna. Poskusi znova.";
            } else {
                location = { latitude: candidate.latitude, longitude: candidate.longitude, accuracy: candidate.accuracy };
                status.textContent = candidate.accuracy > 100 ? "Razdalje so približne." : "Razvrščeno po bližini.";
            }
            render();
        }, () => {
            locate.disabled = false;
            location = null;
            status.textContent = "Lokacija ni na voljo. Lokacije lahko še vedno pregleduješ ali poskusiš znova.";
            render();
        }, { enableHighAccuracy: false, timeout: 10000, maximumAge: 60000 });
    });
})();
