(() => {
    const install = document.getElementById('pwaInstall');
    const button = document.getElementById('pwaInstallButton');
    const help = document.getElementById('pwaInstallHelp');
    const update = document.getElementById('pwaUpdate');
    const network = document.getElementById('pwaNetwork');
    const standalone = () => window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
    let deferred = null;
    function presentation() {
        if (install) install.hidden = standalone();
        if (button) button.hidden = standalone() || !deferred;
        if (help) help.textContent = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1)
            ? 'V Safariju odpri Deli in izberi Dodaj na začetni zaslon.'
            : 'Če brskalnik podpira namestitev, jo najdeš tudi v njegovem meniju. DoggyDrop lahko uporabljaš brez namestitve.';
    }
    window.addEventListener('beforeinstallprompt', event => {
        event.preventDefault(); deferred = event; presentation();
    });
    window.addEventListener('appinstalled', () => { deferred = null; if (install) install.hidden = true; });
    button?.addEventListener('click', async () => {
        const prompt = deferred;
        if (!prompt) return;
        deferred = null; button.disabled = true;
        try { await prompt.prompt(); await prompt.userChoice; } catch { /* menu guidance remains */ }
        finally { button.disabled = false; presentation(); }
    });
    presentation();
    function connection() { if (network) network.hidden = navigator.onLine !== false; }
    window.addEventListener('offline', connection);
    window.addEventListener('online', connection); // No reload, POST retry or sensitive replay.
    document.getElementById('pwaNetworkDismiss')?.addEventListener('click', () => { network.hidden = true; });
    connection();
    if ('serviceWorker' in navigator && window.isSecureContext) {
        navigator.serviceWorker.register('/sw.js', { updateViaCache: 'none' }).then(registration => {
            function available() { if (update) update.hidden = !registration.waiting; }
            available();
            registration.addEventListener('updatefound', () => {
                registration.installing?.addEventListener('statechange', available);
            });
        }).catch(() => { /* Browser/network restrictions never block normal app usage. */ });
    }
})();
