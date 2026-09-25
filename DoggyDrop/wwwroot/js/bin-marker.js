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
                        <path class="map-bin-marker__handle" d="M13 7h6v2h-6z" />
                        <path class="map-bin-marker__lid" d="M7 10h18v3H7z" />
                        <path class="map-bin-marker__body" d="M9 14h14l-1.5 12h-11z" />
                        <path class="map-bin-marker__line" d="M14 17v6m4-6v6" />
                    </svg>
                    <span class="map-bin-marker__status" aria-hidden="true"></span>
                </span>`,
            iconSize: [44, 48],
            iconAnchor: [22, 43],
            popupAnchor: [0, -38]
        });
    }

    window.DoggyDropBinMarker = { createIcon };
})();
