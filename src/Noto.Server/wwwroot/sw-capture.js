// Service worker — cache local fonts, always fetch m.html fresh.
// Bumped cache name to force refresh after moving off external CDNs.
const CACHE_NAME = 'noto-static-v2';

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME).then(cache =>
            cache.addAll([
                '/lib/fonts/jetbrains-mono.css',
                '/lib/fonts/jetbrainsmono-normal.woff2',
                '/lib/fonts/jetbrainsmono-italic.woff2'
            ])
        )
    );
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(names =>
            Promise.all(names.filter(n => n !== CACHE_NAME).map(n => caches.delete(n)))
        ).then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', event => {
    // Never cache API calls or m.html
    if (event.request.url.includes('/api/') || event.request.url.includes('m.html')) return;

    event.respondWith(
        caches.match(event.request).then(response => response || fetch(event.request))
    );
});
