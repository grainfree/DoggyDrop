(() => {
    const form = document.getElementById('contributionForm');
    if (form) {
        const submit = document.getElementById('contributionSubmit');
        const label = document.getElementById('contributionSubmitLabel');
        const spinner = document.getElementById('contributionSpinner');
        const status = document.getElementById('contributionSubmitStatus');
        let submitting = false;
        let generation = 0;
        function resetSubmission() {
            submitting = false;
            submit.disabled = false;
            form.setAttribute('aria-busy', 'false');
            label.textContent = 'Pošlji v pregled';
            spinner.hidden = true;
            status.textContent = '';
        }
        function showFailure(messages, validationDocument) {
            const summary = document.querySelector('.community-errors');
            const list = document.createElement('ul');
            for (const message of messages) {
                const item = document.createElement('li'); item.textContent = message; list.appendChild(item);
            }
            summary.replaceChildren(list);
            summary.classList.remove('validation-summary-valid');
            summary.classList.add('validation-summary-errors');
            // Copy only validation text, never returned HTML, scripts, values or file inputs.
            for (const slot of form.querySelectorAll('[data-valmsg-for]')) {
                const name = slot.getAttribute('data-valmsg-for');
                const source = validationDocument && Array.from(validationDocument.querySelectorAll('[data-valmsg-for]'))
                    .find(item => item.getAttribute('data-valmsg-for') === name);
                slot.textContent = source ? source.textContent : '';
                for (const field of form.elements) if (field.name === name) field.setAttribute('aria-invalid', slot.textContent ? 'true' : 'false');
            }
            summary.focus();
        }
        form.addEventListener('submit', async event => {
            if (submitting) { event.preventDefault(); return; }
            if (event.defaultPrevented) return;
            if (!form.checkValidity()) { event.preventDefault(); form.reportValidity(); return; }
            event.preventDefault();
            // Same multipart POST, antiforgery token and RequestId as the native form fallback.
            // Only the nameless submit button is disabled: photo and field values stay intact.
            submitting = true;
            const attempt = ++generation;
            submit.disabled = true;
            form.setAttribute('aria-busy', 'true');
            label.textContent = 'Pošiljam …';
            spinner.hidden = false;
            status.textContent = 'Pošiljam … Počakaj, da se prispevek pošlje v pregled.';
            let navigating = false;
            try {
                const response = await fetch(form.action, {
                    method: 'POST', body: new FormData(form), credentials: 'same-origin', redirect: 'manual'
                });
                if (attempt !== generation) return;
                // Do not follow the redirect in fetch: that would consume Mine's TempData message.
                // Create's existing successful redirect is Mine. Its auth checks still apply.
                if (response.type === 'opaqueredirect') {
                    window.location.assign(form.dataset.successUrl);
                    navigating = true;
                    return;
                }
                let validationDocument, messages = [];
                if (response.status === 400 && (response.headers.get('content-type') || '').includes('text/html')) {
                    validationDocument = new DOMParser().parseFromString(await response.text(), 'text/html');
                    messages = Array.from(validationDocument.querySelectorAll('.community-errors.validation-summary-errors li'))
                        .map(item => item.textContent.trim()).filter(Boolean);
                }
                if (attempt !== generation) return;
                showFailure(messages.length ? messages : [response.status === 429
                    ? 'Preveč poskusov. Počakaj malo in poskusi znova.'
                    : 'Prispevka ni bilo mogoče poslati. Poskusi znova.'], validationDocument);
            } catch {
                if (attempt === generation) showFailure(['Povezava je bila prekinjena. Preveri povezavo in poskusi znova.']);
            } finally {
                if (!navigating && attempt === generation) resetSubmission();
            }
        });
        // Also unlock a retained page after Back (including Safari's back/forward cache).
        window.addEventListener('pageshow', () => { generation++; resetSubmission(); });
        resetSubmission();
    }
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
