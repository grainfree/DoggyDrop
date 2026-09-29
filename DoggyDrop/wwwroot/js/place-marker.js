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

    // Only an application CSS class can select bundled artwork; never accept SVG/HTML.
    function iconClass(place) {
        return typeof place?.iconClass === "string" && /^dd-place-icon--[a-z-]{1,40}$/.test(place.iconClass)
            ? place.iconClass : "dd-place-icon--other";
    }

    function createIcon(place, { selected = false } = {}) {
        const kind = typeof place?.categoryKey === "string" && /^[a-z][a-z-]{0,30}$/.test(place.categoryKey)
            ? place.categoryKey : "other";
        const symbol = iconClass(place);
        const categoryLabel = typeof place?.categoryLabel === "string" ? place.categoryLabel : "Lokacija";
        const label = typeof place?.name === "string" && place.name ? `${place.name} – ${categoryLabel}` : categoryLabel;
        const commercial = place?.isCommercial === true;
        const featured = commercial && place?.isCurrentlyFeatured === true;
        const logo = commercial ? safeImageUrl(place?.logoUrl) : null;
        const image = logo
            ? `<img class="managed-place-image managed-place-pin__image" src="${escapeAttribute(logo)}" alt="" decoding="async" referrerpolicy="no-referrer">`
            : "";

        return window.L.divIcon({
            className: "",
            iconSize: [52, 52],
            iconAnchor: [26, 26],
            popupAnchor: [0, -28],
            html: `<span class="managed-place-pin managed-place-pin--${kind}${commercial ? "" : " managed-place-pin--destination"}${featured ? " managed-place-pin--featured" : ""}${selected ? " managed-place-pin--selected" : ""}" role="img" aria-label="${escapeAttribute(label)}"><i class="dd-place-icon ${symbol}" aria-hidden="true"></i>${image}</span>`
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

    window.DoggyDropPlaceMarker = { createIcon, attachImage, safeImageUrl, iconClass };
})();
