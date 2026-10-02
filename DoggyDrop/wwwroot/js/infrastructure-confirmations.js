(function (root) {
    "use strict";
    const messages = {
        accepted: "Hvala! Potrditev je zabeležena.", cooldown: "To lokacijo si pred kratkim že potrdil.",
        far: "Za potrditev moraš biti bližje lokaciji.", accuracy: "Lokacije ni mogoče dovolj natančno določiti. Poskusi znova.",
        unavailable: "Lokacija ni več javno na voljo. Osveži zemljevid.",
        denied: "Za to potrditev dovoli enkratno preverjanje lokacije.",
        location: "Lokacije ni bilo mogoče določiti. Poskusi znova.",
        network: "Izida ni mogoče potrditi. Poskusi znova; ponovitev ne podvoji potrditve.",
        busy: "Preveč poskusov. Poskusi čez minuto.", login: "Za potrditev se prijavi."
    };
    function relative(value, now = Date.now()) {
        const date = new Date(value);
        if (!value || !Number.isFinite(date.getTime())) return "";
        // Calendar dates in Slovenia, with a fixed UTC day difference (no DST-length days).
        const day = n => {
            const parts = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Ljubljana", year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(n);
            const get = type => Number(parts.find(p => p.type === type).value);
            return Date.UTC(get("year"), get("month") - 1, get("day"));
        };
        const days = Math.max(0, Math.round((day(new Date(now)) - day(date)) / 86400000));
        return days === 0 ? "danes" : days === 1 ? "včeraj" : days === 2 ? "pred 2 dnevoma" : days < 7 ? `pred ${days} dnevi` :
            `dne ${new Intl.DateTimeFormat("sl-SI", { timeZone: "Europe/Ljubljana", dateStyle: "medium" }).format(date)}`;
    }
    function describe(summary, water, now) {
        const s = summary || {};
        const last = relative(s.lastConfirmedAt, now);
        const observation = last ? `${water ? "Delovanje potrjeno" : "Prisotnost potrjena"} ${last}` : "Še ni potrjeno s strani skupnosti";
        return { primary: s.hasCurrentIssue ? "Prijavljena je težava" : observation,
            secondary: [s.hasCurrentIssue && last ? observation : "", s.recentUniqueConfirmers > 0 ?
                `Različni potrjevalci v zadnjih 30 dneh: ${s.recentUniqueConfirmers}` : ""].filter(Boolean).join(" · ") };
    }
    // Deterministic flow hooks allow tests to resolve geolocation/network in any order.
    function createFlow({ locate, send, state, current, success, schedule = setTimeout, cancelTimer = clearTimeout }) {
        let active = null;
        function cancel() {
            if (!active) return;
            const old = active; active = null; cancelTimer(old.timer); old.controller.abort(); state(old.target, false, "");
        }
        async function start(target) {
            if (active) return false;
            const operation = { target, controller: new AbortController() }; active = operation;
            const valid = () => active === operation && current(target);
            state(target, true, "Preverjam lokacijo …");
            operation.timer = schedule(() => {
                if (active !== operation) return;
                active = null; operation.controller.abort(); state(target, false, messages[operation.sending ? "network" : "location"]);
            }, 30000);
            try {
                const position = await locate();
                if (!valid()) return false;
                operation.sending = true;
                state(target, true, "Potrjujem …");
                const result = await send(target, position, operation.controller.signal);
                if (!valid()) return false;
                if (["accepted", "cooldown"].includes(result.outcome) && result.summary) success(target, result.summary);
                state(target, false, result.outcome === "accepted" && target.kind === "water" ?
                    "Hvala! Zabeležili smo, da pitnik deluje." : messages[result.outcome] || messages.network);
                return result.outcome === "accepted";
            } catch (error) {
                if (valid()) state(target, false, messages[error?.code === 1 ? "denied" : error?.location ? "location" : "network"]);
                return false;
            } finally {
                cancelTimer(operation.timer);
                if (active === operation) { active = null; if (!current(target)) state(target, false, ""); }
            }
        }
        return { start, cancel };
    }
    let settings, flow, listening = false;
    let currentPopup = null;
    // Only the contribution surfaces move/scroll. Navigation framing and route state are untouched.
    function fit(map, popup) {
        if (popup) currentPopup = popup;
        const top = Math.max(16, document.querySelector(".app-topbar")?.getBoundingClientRect().bottom || 0) + 12;
        const viewport = root.visualViewport;
        let bottom = viewport ? viewport.offsetTop + viewport.height : root.innerHeight;
        const nav = document.querySelector(".app-bottom-nav")?.getBoundingClientRect();
        if (nav?.height) bottom = Math.min(bottom, nav.top);
        const navigating = document.body.classList.contains("map-bin-navigation-active");
        if (navigating) {
            const card = document.getElementById("directionsPanel")?.getBoundingClientRect();
            if (card?.height) bottom = Math.min(bottom, card.top - 28); // attribution above the card
        }
        const attribution = map.getContainer().querySelector(".leaflet-control-attribution")?.getBoundingClientRect();
        if (attribution?.height) bottom = Math.min(bottom, attribution.top - 8);
        const panel = document.getElementById("binDetailPanel");
        if (panel) {
            panel.style.bottom = `${Math.max(0, root.innerHeight - bottom + 8)}px`;
            const content = panel.querySelector(".map-bin-detail__panel");
            if (content) content.style.maxHeight = `${Math.max(100, bottom - top - 24)}px`;
        }
        if (currentPopup?.isOpen() && currentPopup.getElement()?.querySelector("[data-confirm-kind]")) {
            const rect = map.getContainer().getBoundingClientRect();
            const height = Math.max(100, Math.min(360, bottom - top - 64));
            const paddingTop = Math.max(16, top - rect.top), paddingBottom = Math.max(16, rect.bottom - bottom + 16);
            if (currentPopup.options.maxHeight !== height || currentPopup.options.autoPanPaddingTopLeft?.[1] !== paddingTop ||
                currentPopup.options.autoPanPaddingBottomRight?.[1] !== paddingBottom) {
                flow?.cancel();
                currentPopup.options.maxHeight = height;
                currentPopup.options.autoPanPaddingTopLeft = [16, paddingTop];
                currentPopup.options.autoPanPaddingBottomRight = [16, paddingBottom];
                currentPopup.update();
            }
        }
    }
    function observeLayout(map) {
        let frame;
        const update = () => { if (frame) return; frame = requestAnimationFrame(() => { frame = null; fit(map); }); };
        const observer = new ResizeObserver(update);
        [document.getElementById("directionsPanel"), document.querySelector(".app-bottom-nav"), document.querySelector(".app-topbar")]
            .filter(Boolean).forEach(node => observer.observe(node));
        root.addEventListener("resize", update); root.visualViewport?.addEventListener("resize", update);
    }
    function render(node, summary) {
        const copy = describe(summary, node.dataset.confirmKind === "water");
        node.replaceChildren();
        const title = document.createElement("p"); title.className = "confirmation-summary"; title.textContent = copy.primary; node.append(title);
        if (copy.secondary) { const text = document.createElement("small"); text.textContent = copy.secondary; node.append(text); }
        const button = document.createElement(settings?.signedIn ? "button" : "a");
        button.className = "confirmation-action";
        const label = node.dataset.confirmKind === "water" ? "Pitnik deluje" : "Koš je še tukaj";
        if (settings?.signedIn) { button.type = "button"; button.dataset.confirmAction = "true"; button.textContent = label; }
        else { button.href = "/Identity/Account/Login?ReturnUrl=%2F"; button.textContent = label + " · prijava"; }
        node.append(button);
        const status = document.createElement("p"); status.className = "confirmation-status"; status.setAttribute("role", "status"); status.setAttribute("aria-live", "polite"); node.append(status);
    }
    function hydrate(scope = document) {
        scope.querySelectorAll("[data-confirm-kind]").forEach(node => {
            const record = settings?.find(node.dataset.confirmKind, Number(node.dataset.confirmId));
            render(node, record?.trust);
        });
    }
    function configure(options) {
        settings = options;
        flow?.cancel();
        flow = createFlow({
            locate: () => new Promise((resolve, reject) => {
                if (!navigator.geolocation) return reject({ location: true });
                navigator.geolocation.getCurrentPosition(p => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude, accuracy: p.coords.accuracy }),
                    e => reject({ code: e.code, location: true }), { enableHighAccuracy: true, maximumAge: 0, timeout: 12000 });
            }),
            send: async (target, position, signal) => {
                const response = await fetch(`/api/confirmations/${target.kind}/${target.id}`, {
                    method: "POST", credentials: "same-origin", cache: "no-store", redirect: "error", signal,
                    headers: { "Content-Type": "application/json", RequestVerificationToken: settings.token }, body: JSON.stringify(position)
                });
                if (response.status === 429) return { outcome: "busy" };
                if ([401, 403].includes(response.status)) return { outcome: "login" };
                return response.json();
            },
            current: t => t.node.isConnected && !t.node.closest("[hidden]"),
            state: (t, busy, text) => {
                t.node.setAttribute("aria-busy", String(busy));
                const button = t.node.querySelector("button");
                if (button) { button.disabled = busy; button.textContent = busy ? text : t.kind === "water" ? "Pitnik deluje" : "Koš je še tukaj"; }
                const status = t.node.querySelector(".confirmation-status"); if (status) status.textContent = busy ? "" : text;
            },
            success: (target, summary) => {
                const record = settings.find(target.kind, target.id); if (record) record.trust = summary;
                render(target.node, summary);
            }
        });
        if (!listening) {
            listening = true;
            document.addEventListener("click", event => {
                const action = event.target.closest?.("[data-confirm-action]"); if (!action) return;
                const node = action.closest("[data-confirm-kind]");
                if (!settings.signedIn || !node) return;
                void flow.start({ node, kind: node.dataset.confirmKind, id: Number(node.dataset.confirmId) });
            });
            root.addEventListener("pagehide", () => flow?.cancel());
        }
    }
    const api = { relative, describe, createFlow, configure, render, hydrate, fit, observeLayout, cancel: () => flow?.cancel() };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.DoggyDropConfirmations = api;
})(typeof window === "undefined" ? globalThis : window);
