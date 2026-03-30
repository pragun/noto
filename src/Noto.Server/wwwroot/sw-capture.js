// Service worker — cache fonts only, always fetch m.html fresh
const CACHE_NAME = 'noto-static-v1';

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME).then(cache =>
            cache.addAll([
                'https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@300;400;500&display=swap'
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
