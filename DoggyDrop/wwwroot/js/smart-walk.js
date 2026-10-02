(function (root) {
    'use strict';
    // Pure request lifecycle is shared by the browser and deterministic Node tests.
    function createRequests(send, hooks) {
        let revision = 0, active = null;
        function cancel() { revision++; active?.abort(); active = null; hooks.busy(false); }
        async function run(payload) {
            if (active) return false;
            const current = ++revision, controller = new AbortController(); active = controller; hooks.busy(true);
            try {
                const result = await send(payload, controller.signal);
                if (current === revision && !controller.signal.aborted) hooks.success(result);
            } catch (error) {
                if (current === revision && !controller.signal.aborted) hooks.failure(error);
            } finally { if (current === revision) { active = null; hooks.busy(false); } }
            return true;
        }
        return { run, cancel, get busy() { return active !== null; } };
    }
    function validResult(result, saved = false) {
        return !!result && (saved || /^[A-F0-9]{48}$/.test(result.token || '')) && Array.isArray(result.points)
            && result.points.length >= 2 && result.points.length <= 20000
            && result.points.every(p => Array.isArray(p) && p.length === 2 && Number.isFinite(p[0]) && Math.abs(p[0]) <= 90 && Number.isFinite(p[1]) && Math.abs(p[1]) <= 180)
            && Number.isFinite(result.distanceMeters) && result.distanceMeters > 0 && Number.isFinite(result.durationSeconds) && result.durationSeconds > 0;
    }
    if (typeof module === 'object' && module.exports) module.exports = { createRequests, validResult };
    if (!root.document?.getElementById('smartWalk')) return;
    const doc = root.document, el = id => doc.getElementById(id), form = el('smartForm');
    const map = root.L.map('smartMap').setView([46.1, 14.8], 8);
    root.DoggyDropBasemap.addTo(map);
    const layers = root.L.layerGroup().addTo(map);
    let start = null, startMarker = null, picking = false, variant = 0, geoRevision = 0, searchRevision = 0, searchAbort = null, currentLine = null;
    const csrf = form.querySelector('input[name="__RequestVerificationToken"]').value;
    function icon(kind) {
        const node = doc.createElement('span');
        node.className = kind === 'park' ? 'dd-place-icon dd-place-icon--dog-park' : 'bi ' + ({ water: 'bi-droplet', bin: 'bi-trash3', start: 'bi-record-circle', finish: 'bi-flag', place: 'bi-geo-alt' }[kind] || 'bi-geo-alt');
        node.setAttribute('aria-hidden', 'true'); return node;
    }
    function marker(point, kind, name) {
        const body = doc.createElement('span'); body.className = 'smart-marker-body'; body.append(icon(kind));
        const label = doc.createElement('span'); label.textContent = name;
        return root.L.marker(point, { title: name, alt: name, icon: root.L.divIcon({ className: 'smart-marker smart-marker-' + (kind === 'start' ? 'start' : 'stop'), html: body, iconSize: [36, 36], iconAnchor: [18, 18] }) }).bindPopup(label);
    }
    function error(message) { el('smartError').textContent = message; el('smartError').hidden = !message; }
    function busy(value) {
        form.setAttribute('aria-busy', String(value)); el('smartGenerate').disabled = value; el('smartAgain').disabled = value;
        el('smartGenerate').textContent = value ? 'Pripravljam predlog …' : 'Predlagaj sprehod';
        el('smartStatus').textContent = value ? 'Iščemo primerno pešpot. To lahko traja nekaj sekund.' : el('smartResult').hidden ? '' : 'Predlog sprehoda je pripravljen.';
    }
    function fit() {
        map.invalidateSize();
        if (currentLine) {
            const attribution = map.getContainer().querySelector('.leaflet-control-attribution');
            map.fitBounds(currentLine.getBounds(), { paddingTopLeft: [32, 40], paddingBottomRight: [32, 40 + (attribution?.offsetHeight || 30)], maxZoom: 17, animate: false });
        }
    }
    function show(result, saved = false) {
        if (!validResult(result, saved)) throw new Error('Prejeli smo neveljaven predlog. Poskusi znova.');
        layers.clearLayers();
        if (startMarker) { map.removeLayer(startMarker); startMarker = null; }
        currentLine = root.L.polyline(result.points, { color: '#216348', weight: 5 }).addTo(layers);
        const first = result.points[0], last = result.points[result.points.length - 1];
        marker(first, 'start', 'Začetek sprehoda').addTo(layers);
        if (map.distance(first, last) > 25 && !(result.highlights || []).some(p => map.distance(last, [p.latitude, p.longitude]) < 25)) marker(last, 'finish', 'Cilj sprehoda').addTo(layers);
        for (const p of (result.highlights || []).slice(0, 5)) {
            if (!Number.isFinite(p.latitude) || !Number.isFinite(p.longitude)) continue;
            marker([p.latitude, p.longitude], p.kind, String(p.name)).addTo(layers);
        }
        el('smartDuration').textContent = `Približno ${Math.ceil(result.durationSeconds / 60)} min`;
        el('smartDistance').textContent = `${(result.distanceMeters / 1000).toLocaleString('sl-SI', { maximumFractionDigits: 1 })} km pešpoti`;
        el('smartFacts').replaceChildren();
        for (const fact of (result.facts || [])) { const li = doc.createElement('li'); li.append(icon(fact.kind), doc.createTextNode(' ' + fact.text)); el('smartFacts').append(li); }
        if (!saved && !result.facts?.length) { const li = doc.createElement('li'); li.textContent = 'Ob tej poti nimamo podatkov o izbranih postankih.'; el('smartFacts').append(li); }
        el('smartNotice').textContent = result.notice; el('smartResult').hidden = false; el('smartFreshActions').hidden = saved;
        if (el('smartSavedStart')) el('smartSavedStart').hidden = !saved;
        el('smartToken').value = saved ? '' : result.token; el('smartResultTitle').textContent = saved ? 'Shranjen sprehod' : 'Predlog sprehoda';
        error(''); fit();
        if (!saved) { el('smartResultTitle').focus({ preventScroll: true }); el('smartResult').scrollIntoView({ block: 'start', behavior: 'instant' }); }
    }
    const requests = createRequests(async (payload, signal) => {
        const response = await root.fetch('/api/smart-walk', { method: 'POST', signal, credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf }, body: JSON.stringify(payload) });
        let body; try { body = await response.json(); } catch { throw new Error('Predloga ni bilo mogoče pripraviti. Poskusi znova.'); }
        if (!response.ok) throw new Error(body.error || 'Predloga ni bilo mogoče pripraviti. Poskusi znova.');
        return body;
    }, { busy, success: show, failure: e => error(e.message || 'Povezava ni uspela. Poskusi znova.') });
    function invalidate() {
        el('smartResult').hidden = true; requests.cancel(); el('smartToken').value = ''; layers.clearLayers(); currentLine = null; variant = 0;
        if (start && !startMarker) startMarker = marker([start.latitude, start.longitude], 'start', 'Izbrano izhodišče').addTo(map);
    }
    function choose(latitude, longitude) {
        if (!Number.isFinite(latitude) || Math.abs(latitude) > 90 || !Number.isFinite(longitude) || Math.abs(longitude) > 180) return;
        geoRevision++; invalidate(); start = { latitude, longitude }; picking = false; el('smartCenter').hidden = true;
        if (startMarker) map.removeLayer(startMarker);
        startMarker = marker([latitude, longitude], 'start', 'Izbrano izhodišče').addTo(map);
        map.setView([latitude, longitude], 15); el('smartLocation').textContent = 'Izhodišče je izbrano na zemljevidu.'; error('');
    }
    el('smartLocate').addEventListener('click', () => {
        const revision = ++geoRevision;
        if (!root.navigator.geolocation) { error('Lokacija ni na voljo. Izberi izhodišče na zemljevidu.'); return; }
        el('smartLocation').textContent = 'Pridobivamo lokacijo …';
        root.navigator.geolocation.getCurrentPosition(p => { if (revision === geoRevision) choose(p.coords.latitude, p.coords.longitude); },
            () => { if (revision === geoRevision) { el('smartLocation').textContent = start ? 'Prejšnje izhodišče ostaja izbrano.' : 'Izhodišče še ni izbrano.'; error('Lokacije ni bilo mogoče pridobiti. Izberi izhodišče na zemljevidu.'); } },
            { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 });
    });
    el('smartPick').addEventListener('click', () => { geoRevision++; picking = true; el('smartCenter').hidden = false; el('smartLocation').textContent = 'Dotakni se izhodišča ali uporabi sredino zemljevida.'; map.getContainer().focus(); });
    map.on('click', e => { if (picking) choose(e.latlng.lat, e.latlng.lng); });
    el('smartCenter').addEventListener('click', () => { const p = map.getCenter(); choose(p.lat, p.lng); });
    form.addEventListener('change', () => { invalidate(); el('smartDestination').hidden = form.elements.walkType.value !== 'destination'; });
    async function generate(again = false) {
        if (requests.busy) return;
        if (!start) { error('Najprej izberi izhodišče.'); return; }
        const walkType = form.elements.walkType.value, placeId = walkType === 'destination' ? Number(el('smartPlace').value) : null;
        if (walkType === 'destination' && !(placeId > 0)) { error('Izberi cilj med najdenimi kraji.'); return; }
        if (again) variant = (variant + 1) % 32;
        error(''); el('smartResult').hidden = true; el('smartToken').value = ''; layers.clearLayers(); currentLine = null;
        await requests.run({ start, minutes: Number(form.elements.minutes.value), walkType, placeId, variant,
            preferences: Array.from(form.querySelectorAll('input[name="preference"]:checked'), i => i.value) });
    }
    form.addEventListener('submit', e => { e.preventDefault(); generate(); });
    el('smartAgain').addEventListener('click', () => generate(true));
    el('smartFind').addEventListener('click', async () => {
        const q = el('smartSearch').value.trim(); if (q.length < 2) { error('Vnesi vsaj dva znaka imena kraja.'); return; }
        const revision = ++searchRevision; searchAbort?.abort(); searchAbort = new AbortController();
        el('smartFind').disabled = true;
        try {
            const response = await root.fetch(`/api/smart-walk/destinations?q=${encodeURIComponent(q)}`, { signal: searchAbort.signal, credentials: 'same-origin' });
            if (!response.ok) throw new Error('Iskanje trenutno ni na voljo.');
            const rows = await response.json(); if (revision !== searchRevision) return;
            invalidate(); el('smartPlace').replaceChildren(new Option(rows.length ? 'Izberi najdeni kraj' : 'Ni najdenih krajev', ''));
            for (const row of rows) el('smartPlace').append(new Option(`${row.name} · ${row.category}`, String(row.id)));
        } catch (e) { if (revision === searchRevision && e.name !== 'AbortError') error(e.message); }
        finally { if (revision === searchRevision) el('smartFind').disabled = false; }
    });
    function dogState() { el('smartStart').disabled = !el('smartDog').value; }
    el('smartDog').addEventListener('change', dogState); dogState();
    // Native POST retains the established redirect/validation flow. Preserve submitter
    // name before disabling it, and restore controls on browser-back/pageshow.
    for (const save of [el('smartSave'), el('smartSavedStart')].filter(Boolean)) {
        save.addEventListener('submit', e => {
            if (save.dataset.busy === 'true') { e.preventDefault(); return; }
            if (!save.checkValidity()) return;
            save.dataset.busy = 'true'; save.setAttribute('aria-busy', 'true');
            if (e.submitter?.name) { const input = doc.createElement('input'); input.type = 'hidden'; input.name = e.submitter.name; input.value = e.submitter.value; input.dataset.submitter = 'true'; save.append(input); }
            for (const button of save.querySelectorAll('button')) button.disabled = true;
        });
    }
    root.addEventListener('pagehide', () => { requests.cancel(); geoRevision++; searchRevision++; searchAbort?.abort(); });
    root.addEventListener('pageshow', () => {
        busy(false); el('smartFind').disabled = false;
        for (const save of [el('smartSave'), el('smartSavedStart')].filter(Boolean)) {
            save.dataset.busy = 'false'; save.removeAttribute('aria-busy'); save.querySelectorAll('[data-submitter]').forEach(p => p.remove()); save.querySelectorAll('button').forEach(b => { b.disabled = false; });
        }
        dogState();
    });
    if (root.ResizeObserver) new root.ResizeObserver(fit).observe(el('smartMap'));
    root.addEventListener('resize', fit);
    const saved = el('smartSaved'); if (saved) { try { show(JSON.parse(saved.textContent), true); } catch { error('Shranjene poti ni bilo mogoče prikazati.'); } }
})(typeof window === 'object' ? window : globalThis);
