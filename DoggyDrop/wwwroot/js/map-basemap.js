(() => {
    "use strict";
    // Domain authentication is configured by the owner. Never accept a tile URL
    // or credential from page/query/user data. Unknown configuration uses OSM.
    const provider = document.currentScript?.dataset.provider === "stadia" ? "stadia" : "osm";
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
    function addTo(map, { maxZoom = 20 } = {}) {
        const selected = provider === "stadia" ? stadia : osm;
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
        if (provider === "stadia") {
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
