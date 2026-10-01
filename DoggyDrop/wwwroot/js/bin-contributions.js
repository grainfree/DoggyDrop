(() => {
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
