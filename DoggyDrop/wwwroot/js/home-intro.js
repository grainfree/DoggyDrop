(() => {
    const intro = document.getElementById("homeIntro");
    // Active walks omit the card on the server; guard the presentation as well.
    if (!intro || document.body.classList.contains("map-walk-active")) return;
    const preferenceKey = "doggydrop.homeIntroDismissed.v1";
    let dismissed = false;
    try { dismissed = localStorage.getItem(preferenceKey) === "true"; } catch { }
    intro.hidden = dismissed;
    document.body.classList.toggle("home-intro-visible", !dismissed);

    const context = document.getElementById("homeContext");
    const community = document.getElementById("communityHeatPanel");
    const communityToggle = document.getElementById("communityHeatToggle");
    communityToggle?.addEventListener("click", () => {
        if (context) context.open = false;
    });
    context?.addEventListener("toggle", () => {
        if (!context.open || !community || !communityToggle) return;
        community.classList.add("is-collapsed");
        community.setAttribute("aria-expanded", "false");
        communityToggle.setAttribute("aria-expanded", "false");
        communityToggle.setAttribute("aria-label", "Prikaži utrip skupnosti");
    });

    function dismiss(moveFocus) {
        intro.hidden = true;
        document.body.classList.remove("home-intro-visible");
        try { localStorage.setItem(preferenceKey, "true"); } catch { }
        if (moveFocus) document.getElementById("map")?.focus({ preventScroll: true });
    }

    document.getElementById("dismissHomeIntro")?.addEventListener("click", () => dismiss(true));
    document.getElementById("homeIntroLogin")?.addEventListener("click", () => dismiss(false));
    // Starting to explore is also completion; keep the chosen control's focus.
    document.querySelectorAll(".map-action-stack a, .map-action-stack button, #filterToggle, #communityHeatToggle")
        .forEach(control => control.addEventListener("click", () => {
            if (!intro.hidden) dismiss(false);
        }));
})();
