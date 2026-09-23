(() => {
    const dataElement = document.getElementById("walkShareData");
    if (!dataElement) return;

    const { asset, url } = JSON.parse(dataElement.textContent);
    const section = document.getElementById("walkSharePreview");
    const heading = document.getElementById("walkShareHeading");
    const closeButton = document.getElementById("closeWalkShare");
    const image = document.getElementById("walkShareImage");
    const loading = document.getElementById("walkShareLoading");
    const feedback = document.getElementById("walkShareFeedback");
    const shareButton = document.getElementById("shareWalkStory");
    const retryButton = document.getElementById("retryWalkShare");
    const saveLink = document.getElementById("saveWalkShare");
    const formats = [...document.querySelectorAll('input[name="walkShareFormat"]')];
    let generation = 0;
    let requestedFormat = "instagram-story";
    let preparedFormat = null;
    let preparedFile = null;
    let objectUrl = null;
    let sharing = false;
    let panelOpen = false;
    let renderBusy = false;
    let renderNeeded = false;
    let photoPromise = null;
    let photoImage = null;
    let cancelPhoto = null;
    let lastTrigger = null;

    function clearPrepared() {
        preparedFile = null;
        preparedFormat = null;
        image.hidden = true;
        image.removeAttribute("src");
        saveLink.hidden = true;
        saveLink.removeAttribute("href");
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        objectUrl = null;
        shareButton.disabled = true;
    }

    function fittedText(ctx, text, x, y, width, size, weight = 800) {
        let fontSize = size;
        do {
            ctx.font = `${weight} ${fontSize}px system-ui, sans-serif`;
            if (ctx.measureText(text).width <= width) break;
            fontSize -= 4;
        } while (fontSize > 38);
        let visible = text;
        while (ctx.measureText(visible).width > width && visible.length > 1) {
            visible = `${visible.slice(0, -2)}…`;
        }
        ctx.fillText(visible, x, y);
    }

    function panel(ctx, top, height) {
        ctx.beginPath();
        ctx.moveTo(0, top + 52);
        ctx.quadraticCurveTo(0, top, 52, top);
        ctx.lineTo(1028, top);
        ctx.quadraticCurveTo(1080, top, 1080, top + 52);
        ctx.lineTo(1080, height);
        ctx.lineTo(0, height);
        ctx.closePath();
        ctx.fillStyle = "#f8fbf8";
        ctx.fill();
    }

    function drawFallback(ctx, top) {
        const background = ctx.createLinearGradient(0, 0, 1080, top);
        background.addColorStop(0, "#164d42");
        background.addColorStop(1, "#3b8061");
        ctx.fillStyle = background;
        ctx.fillRect(0, 0, 1080, top + 60);
        ctx.strokeStyle = "rgba(255,255,255,0.3)";
        ctx.lineWidth = 14;
        ctx.lineCap = "round";
        ctx.setLineDash([30, 26]);
        ctx.beginPath();
        ctx.moveTo(160, top * 0.75);
        ctx.bezierCurveTo(470, top * 0.8, 245, top * 0.21, 810, top * 0.26);
        ctx.stroke();
        ctx.setLineDash([]);
        ctx.fillStyle = "#f3c86c";
        ctx.beginPath();
        ctx.arc(810, top * 0.26, 27, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = "rgba(255,255,255,0.13)";
        ctx.beginPath();
        ctx.arc(540, top * 0.45, 190, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = "#ffffff";
        ctx.textAlign = "center";
        fittedText(ctx, (Array.from((asset.dogName || "").trim())[0] || "P").toUpperCase(), 540, top * 0.55, 220, 230, 900);
        ctx.textAlign = "left";
    }

    function drawPhoto(ctx, photo, top) {
        const scale = Math.max(1080 / photo.width, (top + 60) / photo.height);
        const width = photo.width * scale;
        const height = photo.height * scale;
        ctx.drawImage(photo, (1080 - width) / 2, (top + 60 - height) / 2, width, height);
    }

    function renderCanvas(format, photo) {
        const isPost = format === "post";
        const height = isPost ? 1350 : 1920;
        const top = isPost ? 620 : 1040;
        const ctxCanvas = document.createElement("canvas");
        ctxCanvas.width = 1080;
        ctxCanvas.height = height;
        const ctx = ctxCanvas.getContext("2d");
        if (!ctx) throw new Error("Canvas unavailable");
        ctx.fillStyle = "#164d42";
        ctx.fillRect(0, 0, 1080, height);
        if (photo) drawPhoto(ctx, photo, top);
        else drawFallback(ctx, top);
        panel(ctx, top, height);

        const left = 86;
        ctx.textAlign = "left";
        ctx.fillStyle = "#467566";
        ctx.font = "700 34px system-ui, sans-serif";
        ctx.fillText("SPREHOD · " + asset.date, left, top + (isPost ? 110 : 85));
        ctx.fillStyle = "#173c34";
        fittedText(ctx, asset.dogName || "Pes", left, top + (isPost ? 224 : 210), 910, isPost ? 88 : 110, 900);
        fittedText(ctx, asset.distance, left, top + (isPost ? 390 : 380), 910, isPost ? 134 : 160, 900);
        if (asset.duration) {
            ctx.fillStyle = "#41675b";
            ctx.font = "700 43px system-ui, sans-serif";
            ctx.fillText("Od začetka do konca · " + asset.duration, left, top + (isPost ? 458 : 455));
        }
        if (asset.highlight) {
            ctx.fillStyle = "#ac6a19";
            fittedText(ctx, asset.highlight, left, top + (isPost ? 515 : 515), 910, 42, 750);
        }
        const footerY = isPost ? 1210 : 1615;
        ctx.fillStyle = "#1c5848";
        ctx.font = "900 48px system-ui, sans-serif";
        ctx.fillText("DoggyDrop", left, footerY);
        ctx.fillStyle = "#41675b";
        ctx.font = "600 36px system-ui, sans-serif";
        ctx.fillText("doggydrop.app", left, footerY + 48);
        return ctxCanvas;
    }

    function pngBlob(canvas) {
        return new Promise((resolve, reject) => {
            try {
                canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error("PNG unavailable")), "image/png");
            } catch (error) { reject(error); }
        });
    }

    function loadPhoto() {
        if (photoPromise) return photoPromise;
        if (!asset.photoUrl) return photoPromise = Promise.resolve(null);
        photoPromise = new Promise(resolve => {
            const photo = new Image();
            photoImage = photo;
            let settled = false;
            let timeout;
            const complete = value => {
                if (settled) return;
                settled = true;
                window.clearTimeout(timeout);
                cancelPhoto = null;
                resolve(value);
            };
            cancelPhoto = () => {
                photo.removeAttribute("src");
                complete(null);
            };
            photo.crossOrigin = "anonymous";
            timeout = window.setTimeout(() => {
                photo.removeAttribute("src");
                complete(null);
            }, 8000);
            photo.src = asset.photoUrl;
            photo.decode().then(
                () => complete(photo.width > 0 && photo.height > 0 ? photo : null),
                () => complete(null));
        });
        return photoPromise;
    }

    function releasePhoto() {
        cancelPhoto?.();
        cancelPhoto = null;
        photoImage = null;
        photoPromise = null;
    }

    function requestPrepare() {
        requestedFormat = formats.find(input => input.checked)?.value || "instagram-story";
        ++generation;
        clearPrepared();
        loading.hidden = false;
        retryButton.hidden = true;
        feedback.textContent = "Pripravljam …";
        renderNeeded = true;
        void drainRenderQueue();
    }

    async function drainRenderQueue() {
        if (renderBusy) return;
        renderBusy = true;
        try {
            while (panelOpen && renderNeeded) {
                renderNeeded = false;
                const current = generation;
                const format = requestedFormat;
                try {
                    const photo = await loadPhoto();
                    if (!panelOpen || current !== generation) continue;
                    let blob;
                    let usedPhoto = Boolean(photo);
                    try { blob = await pngBlob(renderCanvas(format, photo)); }
                    catch (error) {
                        if (!photo) throw error;
                        if (!panelOpen || current !== generation) continue;
                        photoImage = null;
                        photoPromise = Promise.resolve(null);
                        blob = await pngBlob(renderCanvas(format, null));
                        usedPhoto = false;
                    }
                    if (!panelOpen || current !== generation) continue;
                    const file = new File([blob], `doggydrop-${format}.png`, { type: "image/png" });
                    const nextUrl = URL.createObjectURL(blob);
                    preparedFile = file;
                    preparedFormat = format;
                    objectUrl = nextUrl;
                    image.src = nextUrl;
                    image.hidden = false;
                    saveLink.href = nextUrl;
                    saveLink.download = file.name;
                    saveLink.hidden = false;
                    shareButton.disabled = sharing;
                    feedback.textContent = usedPhoto ? "Slika je pripravljena." : "Slika brez fotografije je pripravljena.";
                } catch {
                    if (panelOpen && current === generation) {
                        retryButton.hidden = false;
                        feedback.textContent = "Slike ni bilo mogoče pripraviti. Poskusi znova.";
                    }
                } finally {
                    if (panelOpen && current === generation) loading.hidden = true;
                }
            }
        } finally {
            renderBusy = false;
        }
    }

    function disposeSession(hide, restoreFocus) {
        panelOpen = false;
        ++generation;
        renderNeeded = false;
        clearPrepared();
        releasePhoto();
        loading.hidden = true;
        retryButton.hidden = true;
        feedback.textContent = "";
        if (hide) section.hidden = true;
        if (restoreFocus && lastTrigger?.isConnected) lastTrigger.focus();
    }

    document.querySelectorAll("[data-share-walk-story]").forEach(button => {
        button.addEventListener("click", () => {
            lastTrigger = button;
            section.hidden = false;
            if (!panelOpen) {
                panelOpen = true;
                requestPrepare();
            }
            section.scrollIntoView({ behavior: "smooth", block: "start" });
            heading.focus({ preventScroll: true });
        });
    });
    formats.forEach(input => input.addEventListener("change", () => {
        if (panelOpen && input.checked) requestPrepare();
    }));
    retryButton.addEventListener("click", () => { if (panelOpen) requestPrepare(); });
    closeButton.addEventListener("click", () => { if (!sharing) disposeSession(true, true); });
    section.addEventListener("keydown", event => {
        if (event.key === "Escape" && !sharing) {
            event.preventDefault();
            disposeSession(true, true);
        }
    });
    document.getElementById("copyWalkShareText").addEventListener("click", async () => {
        try {
            await navigator.clipboard.writeText(`${asset.text} ${url}`);
            feedback.textContent = "Povzetek je kopiran.";
        } catch { feedback.textContent = "Kopiranje ni uspelo."; }
    });
    shareButton.addEventListener("click", async () => {
        if (sharing || !preparedFile || preparedFormat !== requestedFormat) return;
        sharing = true;
        shareButton.disabled = true;
        closeButton.disabled = true;
        formats.forEach(input => { input.disabled = true; });
        try {
            const file = preparedFile;
            if (navigator.share) {
                let canShareFile = false;
                try { canShareFile = navigator.canShare?.({ files: [file] }) === true; }
                catch { /* Fall back to the platform's link share. */ }
                if (canShareFile) {
                    await navigator.share({ files: [file], title: "DoggyDrop sprehod", text: asset.text });
                } else {
                    await navigator.share({ title: "DoggyDrop sprehod", text: asset.text, url });
                }
                feedback.textContent = "Pripravljeno za deljenje.";
            } else {
                await navigator.clipboard.writeText(`${asset.text} ${url}`);
                feedback.textContent = "Povzetek je kopiran. Sliko lahko shraniš ali jo deliš iz predogleda.";
            }
        } catch (error) {
            if (error?.name !== "AbortError") feedback.textContent = "Deljenje ni uspelo. Poskusi shraniti sliko.";
        } finally {
            sharing = false;
            formats.forEach(input => { input.disabled = false; });
            closeButton.disabled = false;
            shareButton.disabled = !preparedFile || preparedFormat !== requestedFormat;
        }
    });
    window.addEventListener("pagehide", () => {
        disposeSession(false, false);
    });
    window.addEventListener("pageshow", () => {
        if (!section.hidden && !panelOpen) {
            panelOpen = true;
            requestPrepare();
        }
    });
})();
