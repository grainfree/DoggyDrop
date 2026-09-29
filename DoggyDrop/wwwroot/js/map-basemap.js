(() => {
    "use strict";
    // The shared server partial emits an allowlisted provider and, only for CARTO,
    // its browser-public restricted key. Never accept arbitrary URLs/query inputs.
    const config = document.currentScript?.dataset;
    const cartoKey = config?.cartoApiKey;
    const validCartoKey = typeof cartoKey === "string" && cartoKey.length > 0 && cartoKey.length <= 512
        && !/[^A-Za-z0-9._~-]/.test(cartoKey);
    const provider = config?.provider === "stadia" ? "stadia"
        : config?.provider === "carto" && validCartoKey ? "carto" : "osm";
    const osm = {
        url: "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        nativeZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
    };
    const stadia = {
        url: "https://tiles-eu.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png",
        nativeZoom: 20,
        attribution: '&copy; <a href="https://stadiamaps.com/attribution/">Stadia Maps</a> &copy; <a href="https://openmaptiles.org/">OpenMapTiles</a> &copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    };
    const carto = {
        url: "https://basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png?key=" + encodeURIComponent(validCartoKey ? cartoKey : ""),
        nativeZoom: 20,
        attribution: osm.attribution + ', &copy; <a href="https://carto.com/attribution/">CARTO</a>'
    };
    function addTo(map, { maxZoom = 20 } = {}) {
        // Leaflet permits disabling its framework prefix independently of layer credits.
        map.attributionControl?.setPrefix(false);
        const selected = provider === "carto" ? carto : provider === "stadia" ? stadia : osm;
        const container = map.getContainer();
        container.classList.add("doggydrop-basemap");
        container.dataset.basemap = provider;
        const layer = window.L.tileLayer(selected.url, {
            maxZoom: maxZoom === 19 ? 19 : 20,
            maxNativeZoom: selected.nativeZoom,
            attribution: selected.attribution,
            // {r} provides a single high-DPI image, not detectRetina's four tiles.
            detectRetina: false,
            referrerPolicy: "strict-origin-when-cross-origin"
        });
        if (provider !== "osm") {
            let errors = 0;
            const fallback = () => {
                if (++errors < 3) return;
                layer.off("tileerror", fallback);
                // Keep the same layer so Details diagnostics and map ownership
                // remain valid. One fallback per map, no retry/provider loop.
                if (map.hasLayer(layer)) {
                    map.attributionControl?.removeAttribution(layer.options.attribution);
                    map.attributionControl?.addAttribution(osm.attribution);
                }
                layer.options.attribution = osm.attribution;
                layer.options.maxNativeZoom = osm.nativeZoom;
                container.dataset.basemap = "osm";
                layer.setUrl(osm.url);
            };
            layer.on("tileerror", fallback);
        }
        return [layer.addTo(map)];
    }
    window.DoggyDropBasemap = { addTo };
})();
