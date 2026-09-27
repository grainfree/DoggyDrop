// Saved cards use the same category fallback as Discovery, without requesting location.
(() => {
    document.querySelectorAll(".place-discovery-card__logo img").forEach(image => {
        image.addEventListener("error", () => { image.hidden = true; }, { once: true });
        if (image.complete && image.naturalWidth === 0) image.hidden = true;
    });
})();
