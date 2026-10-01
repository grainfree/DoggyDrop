(() => {
    // Public infrastructure only. User, route and social layers never enter this group.
    function create(map) {
        const L = window.L;
        let focused = null;
        let focusedPane = null;
        let movingFocus = false;
        const owners = new Map();
        // Only the selected target gets priority over public clusters, below navigation/user panes.
        map.createPane("focusedLocations").style.zIndex = 615;
        const focusLayer = L.layerGroup().addTo(map);
        const ClusterIcon = L.DivIcon.extend({
            createIcon(oldIcon) {
                const element = L.DivIcon.prototype.createIcon.call(this, oldIcon);
                element.setAttribute("aria-label", this.options.label);
                element.setAttribute("role", "button");
                return element;
            }
        });
        const group = L.markerClusterGroup({
            maxClusterRadius: zoom => zoom < 14 ? 64 : zoom < 17 ? 48 : 22,
            // At street zoom only overlapping targets group; retain spiderfy for coincident points.
            showCoverageOnHover: false,
            animate: false,
            animateAddingMarkers: false,
            removeOutsideVisibleBounds: true,
            spiderfyDistanceMultiplier: 1.6,
            spiderLegPolylineOptions: { weight: 1.5, color: "#286b52", opacity: 0.65 },
            clusterPane: "placeMarkers",
            iconCreateFunction: cluster => {
                const count = cluster.getChildCount();
                const size = count < 10 ? 44 : count < 100 ? 48 : 52;
                const element = document.createElement("span");
                element.className = "home-location-cluster__count";
                element.textContent = String(count);
                element.setAttribute("aria-hidden", "true");
                const last = count % 100;
                const noun = last === 1 ? "lokacija" : last === 2 ? "lokaciji" : last === 3 || last === 4 ? "lokacije" : "lokacij";
                return new ClusterIcon({ className: "home-location-cluster", html: element,
                    label: `${count} ${noun} na tem območju. Povečaj ali razpri skupino.`,
                    iconSize: [size, size], iconAnchor: [size / 2, size / 2] });
            }
        }).addTo(map);

        function visible(marker) { return owners.get(marker)?.active === true; }
        function release(marker = focused) {
            if (movingFocus || !marker || focused !== marker) return;
            focused = null;
            focusLayer.removeLayer(marker);
            marker.options.pane = focusedPane;
            focusedPane = null;
            if (visible(marker)) group.addLayer(marker);
        }
        function pin(marker) {
            if (!visible(marker) || marker === focused) return;
            release();
            movingFocus = true;
            focused = marker;
            focusedPane = marker.options.pane;
            // Removing a spiderfied marker restores its real coordinate before highlighting it.
            const popupWasOpen = marker.isPopupOpen?.();
            group.removeLayer(marker);
            marker.options.pane = "focusedLocations";
            focusLayer.addLayer(marker);
            if (popupWasOpen) marker.openPopup();
            movingFocus = false;
        }
        function reveal(marker, callback = () => {}) {
            if (!marker || !visible(marker)) return;
            // Supported removeLayer restores spiderfied coordinates, then the focus layer
            // reveals the real marker synchronously. No late zoom callback can reopen an old target.
            pin(marker);
            map.setView(marker.getLatLng(), Math.max(map.getZoom(), 16), { animate: false });
            callback();
        }

        // A logical layer preserves Home's existing map.hasLayer/addTo/remove filter contract.
        // It never adds its member markers directly to the map, so counts cannot include hidden layers.
        const LocationLayer = L.Layer.extend({
            initialize() { this.members = new Set(); this.active = false; },
            onAdd() {
                this.active = true;
                group.addLayers(this.getLayers());
            },
            onRemove() {
                this.active = false;
                if (this.members.has(focused)) release();
                group.removeLayers(this.getLayers());
            },
            addLayer(marker) {
                const owner = owners.get(marker);
                if (owner && owner !== this) throw new Error("Location marker already belongs to a layer");
                if (this.members.has(marker)) return this;
                owners.set(marker, this);
                this.members.add(marker);
                if (this.active) group.addLayer(marker);
                return this;
            },
            removeLayer(marker) {
                if (!this.members.has(marker)) return this;
                this.members.delete(marker);
                owners.delete(marker);
                release(marker);
                group.removeLayer(marker);
                return this;
            },
            getLayers() { return Array.from(this.members); },
            // Atomic replacement for future public-data refresh/category subsets, not append-on-refresh.
            setLayers(markers) {
                const next = new Set(markers);
                for (const marker of next) {
                    if (owners.has(marker) && owners.get(marker) !== this)
                        throw new Error("Location marker already belongs to a layer");
                }
                const removed = this.getLayers().filter(marker => !next.has(marker));
                for (const marker of removed) {
                    this.members.delete(marker);
                    owners.delete(marker);
                    release(marker);
                }
                if (this.active) group.removeLayers(removed);
                const added = Array.from(next).filter(marker => !this.members.has(marker));
                for (const marker of added) { owners.set(marker, this); this.members.add(marker); }
                if (this.active) group.addLayers(added);
                return this;
            },
            clearLayers() { return this.setLayers([]); }
        });

        // The default Leaflet Enter handler remains. Add Space without duplicating Enter activation.
        const container = map.getContainer();
        const keydown = event => {
            const target = event.target;
            if (event.key === " " && target?.classList.contains("home-location-cluster")) {
                event.preventDefault();
                target.click();
            }
        };
        container.addEventListener("keydown", keydown);
        const scale = () => {
            container.classList.toggle("home-map--broad", map.getZoom() < 14);
            container.classList.toggle("home-map--mid", map.getZoom() >= 14 && map.getZoom() < 17);
        };
        map.on("zoomend", scale);
        scale();
        map.on("unload", () => container.removeEventListener("keydown", keydown));
        return { createLayer: () => new LocationLayer(), reveal, pin, release };
    }
    window.DoggyDropHomeLocations = { create };
})();
