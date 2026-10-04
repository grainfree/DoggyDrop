// The ONLY cached responses are these public offline assets. No runtime caching.
// Bump the cache version when changing the offline document. Waiting workers use
// the normal browser lifecycle: no skipWaiting, clients.claim or page reload.
const CACHE = 'doggydrop-offline-v1';
const OFFLINE = '/offline.html';
const ASSETS = [OFFLINE, '/css/pwa.css'];
self.addEventListener('install', event => {
    event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(ASSETS)));
});
self.addEventListener('activate', event => {
    event.waitUntil(caches.keys().then(keys => Promise.all(keys
        .filter(key => key.startsWith('doggydrop-offline-') && key !== CACHE)
        .map(key => caches.delete(key)))));
});
self.addEventListener('fetch', event => {
    const request = event.request;
    const url = new URL(request.url);
    if (request.method !== 'GET' || url.origin !== self.location.origin) return;
    if (request.mode === 'navigate') {
        event.respondWith(fetch(request).catch(async () =>
            (await caches.open(CACHE)).match(OFFLINE)));
    } else if (ASSETS.includes(url.pathname) && !url.search) {
        event.respondWith(fetch(request).catch(async () =>
            (await caches.open(CACHE)).match(url.pathname)));
    }
});
