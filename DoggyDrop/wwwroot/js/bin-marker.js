(() => {
    function createIcon(bin, { selected = false } = {}) {
        const statusClass = bin.status === "missing"
            ? " map-bin-marker--missing"
            : bin.status === "full"
                ? " map-bin-marker--full"
                : "";

        return window.L.divIcon({
            className: "",
            html: `
                <span class="map-bin-marker${statusClass}${selected ? " map-bin-marker--selected" : ""}" role="img" aria-label="Pasji koš">
                    <svg class="map-bin-marker__icon" viewBox="0 0 32 32" aria-hidden="true" focusable="false">
                        <path class="map-bin-marker__handle" d="M12.5 9.5c.7-2 2.1-3 3.5-3s2.8 1 3.5 3" />
                        <path class="map-bin-marker__lid" d="M9 11h14" />
                        <path class="map-bin-marker__body" d="M11 13h10l-1.1 11h-7.8L11 13z" />
                        <path class="map-bin-marker__line" d="M14 16.5v4.8M18 16.5v4.8" />
                    </svg>
                    <span class="map-bin-marker__status" aria-hidden="true"></span>
                </span>`,
            iconSize: [44, 48],
            iconAnchor: [22, 45],
            popupAnchor: [0, -40]
        });
    }

    window.DoggyDropBinMarker = { createIcon };
})();
