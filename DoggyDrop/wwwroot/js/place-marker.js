(() => {
    function safeImageUrl(value) {
        if (typeof value !== "string" || !value || /[\s\\\u0000-\u001f]/.test(value)) return null;
        try {
            const url = new URL(value);
            return url.protocol === "https:" && url.hostname && !url.username && !url.password ? value : null;
        } catch {
            return null;
        }
    }

    function escapeAttribute(value) {
        return String(value).replaceAll("&", "&amp;").replaceAll('"', "&quot;")
            .replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll("'", "&#39;");
    }

    function createIcon(place, { selected = false } = {}) {
        const category = Number(place?.category);
        const kind = category === 1 ? "veterinarian" : category === 2 ? "pet-shop" : "other";
        const label = category === 1 ? "Veterinar" : category === 2 ? "Trgovina za male živali" : "Lokacija";
        const symbol = category === 1 ? "bi-heart-pulse-fill" : category === 2 ? "bi-bag-fill" : "bi-geo-alt-fill";
        const logo = safeImageUrl(place?.imageUrl);
        const image = logo
            ? `<img class="managed-place-image managed-place-pin__image" src="${escapeAttribute(logo)}" alt="" decoding="async" referrerpolicy="no-referrer">`
            : "";

        return window.L.divIcon({
            className: "",
            iconSize: [52, 52],
            iconAnchor: [26, 26],
            popupAnchor: [0, -28],
            html: `<span class="managed-place-pin managed-place-pin--${kind}${selected ? " managed-place-pin--selected" : ""}" role="img" aria-label="${label}"><i class="bi ${symbol}" aria-hidden="true"></i>${image}</span>`
        });
    }

    function attachImage(container) {
        const image = container?.querySelector(".managed-place-image");
        if (!image || image.dataset.placeImageBound) return;
        image.dataset.placeImageBound = "1";
        const reveal = () => image.parentElement.classList.add("has-image");
        const fallback = () => {
            image.hidden = true;
            image.parentElement.classList.remove("has-image");
        };
        image.addEventListener("load", reveal, { once: true });
        image.addEventListener("error", fallback, { once: true });
        if (image.complete) {
            if (image.naturalWidth > 0) reveal();
            else fallback();
        }
    }

    window.DoggyDropPlaceMarker = { createIcon, attachImage, safeImageUrl };
})();
