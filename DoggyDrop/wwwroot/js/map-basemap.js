(() => {
    function addTo(map, publicCartoKey, separateLabels = false) {
        const key = typeof publicCartoKey === "string" ? publicCartoKey.trim() : "";
        if (!key) {
            return [window.L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
                maxZoom: 20,
                maxNativeZoom: 19,
                attribution: "&copy; OpenStreetMap contributors"
            }).addTo(map)];
        }

        const query = `?key=${encodeURIComponent(key)}`;
        const style = separateLabels ? "voyager_nolabels" : "voyager";
        const layers = [window.L.tileLayer(`https://{s}.basemaps.cartocdn.com/rastertiles/${style}/{z}/{x}/{y}{r}.png${query}`, {
            subdomains: "abcd",
            maxZoom: 20,
            attribution: "&copy; OpenStreetMap contributors &copy; CARTO"
        }).addTo(map)];
        if (separateLabels) {
            layers.push(window.L.tileLayer(`https://{s}.basemaps.cartocdn.com/rastertiles/voyager_only_labels/{z}/{x}/{y}{r}.png${query}`, {
                subdomains: "abcd",
                maxZoom: 20,
                pane: "tilePane",
                opacity: 0.72,
                attribution: "&copy; OpenStreetMap contributors &copy; CARTO"
            }).addTo(map));
        }
        return layers;
    }

    window.DoggyDropBasemap = { addTo };
})();
