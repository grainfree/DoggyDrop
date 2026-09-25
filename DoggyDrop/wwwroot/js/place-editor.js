(() => {
    const logoFile = document.getElementById("LogoFile");
    const logoRemove = document.getElementById("RemoveLogo");
    const logoPreview = document.getElementById("placeLogoPreview");
    const logoImage = document.getElementById("placeLogoImage");
    const logoName = document.getElementById("placeLogoFileName");
    const savedLogoUrl = logoImage?.getAttribute("src");
    let previewUrl = null;
    const restoreLogo = () => {
        if (logoImage) logoImage.src = savedLogoUrl || "";
        if (logoPreview) logoPreview.hidden = !savedLogoUrl || Boolean(logoRemove?.checked);
    };
    logoFile?.addEventListener("change", () => {
        if (previewUrl) URL.revokeObjectURL(previewUrl);
        previewUrl = null;
        const file = logoFile.files?.[0];
        logoName.textContent = file?.name || "";
        if (file && ["image/png", "image/jpeg", "image/webp"].includes(file.type) && file.size <= 5 * 1024 * 1024) {
            previewUrl = URL.createObjectURL(file);
            logoImage.src = previewUrl;
            logoPreview.hidden = false;
            if (logoRemove) logoRemove.checked = false;
        } else restoreLogo();
    });
    logoRemove?.addEventListener("change", () => {
        if (logoRemove.checked) {
            logoFile.value = "";
            logoName.textContent = "";
            logoPreview.hidden = true;
            if (previewUrl) URL.revokeObjectURL(previewUrl);
            previewUrl = null;
        } else restoreLogo();
    });
    window.addEventListener("pagehide", () => { if (previewUrl) URL.revokeObjectURL(previewUrl); });
    const form = document.querySelector(".places-form");
    const submitButton = form?.querySelector('button[type="submit"]');
    const submitLabel = submitButton?.textContent;
    let submitting = false;
    form?.addEventListener("submit", event => {
        if (submitting) {
            event.preventDefault();
            return;
        }
        submitting = true;
        if (submitButton) {
            submitButton.disabled = true;
            submitButton.textContent = "Shranjujem ...";
            submitButton.setAttribute("aria-busy", "true");
        }
    });
    window.addEventListener("pageshow", () => {
        submitting = false;
        if (submitButton) {
            submitButton.disabled = false;
            submitButton.textContent = submitLabel;
            submitButton.removeAttribute("aria-busy");
        }
    });

    const element = document.getElementById("placePickerMap");
    const latitude = document.getElementById("Latitude");
    const longitude = document.getElementById("Longitude");
    const status = document.getElementById("placePickerStatus");
    if (!element || !latitude || !longitude || !window.L) return;

    // Keep Create aligned with the Home map; Edit is centered on its saved point below.
    const defaultMapCenter = [46.5547, 15.6459];
    const map = L.map(element).setView(defaultMapCenter, 14);
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 19,
        attribution: "&copy; OpenStreetMap"
    }).addTo(map);
    let marker = null;

    function showPoint(lat, lng, moveMap) {
        if (!Number.isFinite(lat) || !Number.isFinite(lng) || Math.abs(lat) > 90 || Math.abs(lng) > 180) return;
        if (marker) marker.setLatLng([lat, lng]);
        else marker = L.circleMarker([lat, lng], {
            radius: 10, color: "#fff", weight: 3, fillColor: "#16805d", fillOpacity: 1
        }).addTo(map);
        if (moveMap) map.setView([lat, lng], Math.max(map.getZoom(), 16));
    }

    function enteredPoint() {
        if (!latitude.value || !longitude.value) return;
        showPoint(Number(latitude.value), Number(longitude.value), true);
    }

    map.on("click", event => {
        latitude.value = event.latlng.lat.toFixed(6);
        longitude.value = event.latlng.lng.toFixed(6);
        showPoint(event.latlng.lat, event.latlng.lng, false);
        status.textContent = "Točka je izbrana.";
    });
    latitude.addEventListener("change", enteredPoint);
    longitude.addEventListener("change", enteredPoint);
    enteredPoint();

    document.getElementById("placeUseLocation")?.addEventListener("click", () => {
        if (!navigator.geolocation || !window.isSecureContext) {
            status.textContent = "Trenutna lokacija ni na voljo. Izberi točko na zemljevidu.";
            return;
        }
        status.textContent = "Pridobivam lokacijo ...";
        navigator.geolocation.getCurrentPosition(position => {
            const { latitude: lat, longitude: lng, accuracy } = position.coords;
            if (!Number.isFinite(lat) || !Number.isFinite(lng) || !Number.isFinite(accuracy) || accuracy > 100) {
                status.textContent = "Lokacija ni dovolj natančna. Izberi točko na zemljevidu.";
                return;
            }
            latitude.value = lat.toFixed(6);
            longitude.value = lng.toFixed(6);
            showPoint(lat, lng, true);
            status.textContent = "Točka je izbrana. Shrani lokacijo.";
        }, () => { status.textContent = "Lokacije ni bilo mogoče pridobiti. Izberi točko na zemljevidu."; }, {
            enableHighAccuracy: true, maximumAge: 0, timeout: 10000
        });
    });
    requestAnimationFrame(() => map.invalidateSize());
    if (window.ResizeObserver) {
        new window.ResizeObserver(() => map.invalidateSize({ pan: false })).observe(element);
    }
})();
