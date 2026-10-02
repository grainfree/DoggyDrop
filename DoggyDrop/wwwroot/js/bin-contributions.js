(() => {
    // Native picker remains the submission control; previews stay on this device.
    const photo = document.getElementById('contributionPhoto');
    if (photo) {
        const preview = document.getElementById('photoPreview');
        const image = document.getElementById('photoPreviewImage');
        const remove = document.getElementById('photoRemove');
        const label = document.getElementById('photoChooseLabel');
        const feedback = document.getElementById('photoSelectionStatus');
        let objectUrl;
        function clearPreview() {
            image.onload = image.onerror = null;
            image.removeAttribute('src'); preview.hidden = true;
            if (objectUrl) URL.revokeObjectURL(objectUrl);
            objectUrl = null;
        }
        function updatePreview() {
            clearPreview();
            const file = photo.files[0];
            remove.hidden = !file;
            label.textContent = file ? 'Zamenjaj fotografijo' : 'Fotografiraj ali izberi fotografijo';
            feedback.textContent = file ? 'Fotografija je izbrana. Pred pošiljanjem preveri predogled.' : '';
            // A local preview is convenience only; all upload validation stays on the server.
            if (!file) return;
            if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type) || file.size > 12 * 1024 * 1024) {
                feedback.textContent = 'Predogled ni na voljo. Izberi JPG, PNG ali WebP do 12 MiB.';
                return;
            }
            const currentUrl = objectUrl = URL.createObjectURL(file);
            image.onload = () => { if (objectUrl === currentUrl) preview.hidden = false; };
            image.onerror = () => {
                if (objectUrl !== currentUrl) return;
                clearPreview(); feedback.textContent = 'Predogleda ni mogoče prikazati. Izberi drugo fotografijo.';
            };
            image.src = currentUrl;
        }
        photo.addEventListener('change', updatePreview);
        remove.addEventListener('click', () => {
            clearPreview(); photo.value = ''; remove.hidden = true;
            label.textContent = 'Fotografiraj ali izberi fotografijo';
            feedback.textContent = 'Fotografija je odstranjena.'; photo.focus();
        });
        window.addEventListener('pagehide', clearPreview);
        window.addEventListener('pageshow', event => {
            if (event.persisted && photo.files.length) updatePreview();
        });
    }
    const reason = document.getElementById('contributionReason');
    if (!reason) return;
    const section = document.getElementById('locationProposal');
    const lat = document.getElementById('proposalLatitude'), lon = document.getElementById('proposalLongitude');
    const status = document.getElementById('contributionLocationStatus');
    let map, marker;
    function select(latitude, longitude) {
        lat.value = latitude.toFixed(6); lon.value = longitude.toFixed(6);
        if (marker) marker.setLatLng([latitude, longitude]);
        else marker = L.marker([latitude, longitude], { draggable: true }).addTo(map).on('dragend', () => {
            const point = marker.getLatLng(); select(point.lat, point.lng);
        });
    }
    function update() {
        section.hidden = reason.value !== 'WRONG_LOCATION';
        document.getElementById('duplicateProposal').hidden = reason.value !== 'DUPLICATE';
        if (section.hidden) return;
        if (!map) {
            const current = [Number(section.dataset.lat), Number(section.dataset.lon)];
            map = L.map('contributionMap').setView(current, 18);
            DoggyDropBasemap.addTo(map, { maxZoom: 20 });
            L.circleMarker(current).addTo(map).bindPopup('Trenutna lokacija');
            map.on('click', e => select(e.latlng.lat, e.latlng.lng));
            if (lat.value && lon.value && Number.isFinite(Number(lat.value)) && Number.isFinite(Number(lon.value))) select(Number(lat.value), Number(lon.value));
        }
        requestAnimationFrame(() => map.invalidateSize());
    }
    reason.addEventListener('change', update);
    for (const field of [lat, lon]) field.addEventListener('change', () => {
        if (map && lat.value && lon.value && Number.isFinite(Number(lat.value)) && Number.isFinite(Number(lon.value)) &&
            Math.abs(Number(lat.value)) <= 90 && Math.abs(Number(lon.value)) <= 180) select(Number(lat.value), Number(lon.value));
    });
    document.getElementById('contributionLocate').addEventListener('click', () => {
        if (!navigator.geolocation || !window.isSecureContext) { status.textContent = 'Lokacijo označi na zemljevidu.'; return; }
        status.textContent = 'Iščem lokacijo …';
        navigator.geolocation.getCurrentPosition(p => {
            select(p.coords.latitude, p.coords.longitude); map.setView(marker.getLatLng(), 18); status.textContent = 'Preveri predlagani položaj.';
        }, () => { status.textContent = 'Lokacije ni mogoče pridobiti. Označi jo na zemljevidu.'; }, { timeout: 10000, maximumAge: 30000 });
    });
    update();
})();
