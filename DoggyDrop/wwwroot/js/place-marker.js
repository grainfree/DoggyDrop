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
        const kind = typeof place?.categoryKey === "string" && /^[a-z][a-z-]{0,30}$/.test(place.categoryKey)
            ? place.categoryKey : "other";
        const symbol = typeof place?.iconClass === "string" && /^bi-[a-z0-9-]{1,40}$/.test(place.iconClass)
            ? place.iconClass : "bi-geo-alt-fill";
        const label = typeof place?.categoryLabel === "string" ? place.categoryLabel : "Lokacija";
        const commercial = place?.isCommercial === true;
        const logo = commercial ? safeImageUrl(place?.logoUrl) : null;
        const image = logo
            ? `<img class="managed-place-image managed-place-pin__image" src="${escapeAttribute(logo)}" alt="" decoding="async" referrerpolicy="no-referrer">`
            : "";

        return window.L.divIcon({
            className: "",
            iconSize: [52, 52],
            iconAnchor: [26, 26],
            popupAnchor: [0, -28],
            html: `<span class="managed-place-pin managed-place-pin--${kind}${commercial ? "" : " managed-place-pin--destination"}${selected ? " managed-place-pin--selected" : ""}" role="img" aria-label="${escapeAttribute(label)}"><i class="bi ${symbol}" aria-hidden="true"></i>${image}</span>`
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
