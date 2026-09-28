(() => {
    "use strict";
    function element(tag, className, text) {
        const node = document.createElement(tag);
        node.className = className;
        if (text != null) node.textContent = String(text);
        return node;
    }

    function create(place, { adminEditBase = null, navigate } = {}) {
        const id = Number(place?.id);
        if (!Number.isSafeInteger(id) || id <= 0) return null;
        const card = element("article", "managed-place-popup");
        const header = element("div", "managed-place-popup__header");
        const media = element("span", "managed-place-popup__media");
        media.setAttribute("aria-hidden", "true");
        const symbol = /^bi-[a-z0-9-]{1,40}$/.test(place.iconClass || "") ? place.iconClass : "bi-geo-alt-fill";
        media.append(element("i", "bi " + symbol));
        const logo = place.isCommercial === true ? window.DoggyDropPlaceMarker.safeImageUrl(place.logoUrl) : null;
        if (logo) {
            const image = element("img", "managed-place-image");
            image.src = logo; image.alt = ""; image.decoding = "async"; image.referrerPolicy = "no-referrer";
            media.append(image);
        }
        header.append(media, element("span", "managed-place-popup__category", place.categoryLabel || "Lokacija"));
        card.append(header, element("h3", "managed-place-popup__name", place.name));
        if (place.isCommercial === true && place.isCurrentlyFeatured === true) {
            const badge = element("span", "place-featured-badge", "Izpostavljeno");
            badge.title = "Promocijska izpostavitev; ne pomeni priporočila DoggyDrop.";
            card.append(badge);
        }
        if (typeof place.address === "string" && place.address.trim())
            card.append(element("p", "managed-place-popup__address", place.address));
        const actions = element("div", "managed-place-popup__actions");
        const details = element("a", "", "Podrobnosti");
        // Server generates the friendly route. Validate its shape and exact Place ID;
        // never use WebsiteUrl or an arbitrary supplied URL as a navigation target.
        details.href = typeof place.detailsUrl === "string" &&
            new RegExp(`^/lokacije/${id}/[a-z0-9-]+$`).test(place.detailsUrl)
            ? place.detailsUrl : `/Places/Details/${id}`;
        const directions = element("button", "managed-place-popup__directions", "Navodila");
        directions.type = "button";
        directions.addEventListener("click", () => navigate?.(id));
        actions.append(details, directions); card.append(actions);
        // The Home document supplies this route only for an authenticated Admin.
        if (adminEditBase === "/AdminPlaces/Edit") {
            const edit = element("a", "managed-place-popup__edit", "Uredi lokacijo");
            edit.href = `${adminEditBase}/${id}?returnTo=map`;
            card.append(edit);
        }
        return card;
    }
    window.DoggyDropPlacePopup = { create };
})();
