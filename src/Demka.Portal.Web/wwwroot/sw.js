/* Service worker: keeps the app shell on the phone so it opens on weak mobile data.
   Data (/api) is never cached here; the app keeps its own copy of the last figures. Bump VERSION on every release. */
const VERSION = 'demka-shell-v4';
const SHELL = ['/', '/index.html', '/app.css?v=4', '/app.js?v=4', '/manifest.webmanifest', '/logo.webp', '/icons/icon.svg', '/icons/icon-192.png', '/icons/icon-512.png'];

self.addEventListener('install', (e) => {
  e.waitUntil(caches.open(VERSION).then((c) => c.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (e) => {
  e.waitUntil(caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== VERSION).map((k) => caches.delete(k))))
    .then(() => self.clients.claim()));
});

self.addEventListener('fetch', (e) => {
  const url = new URL(e.request.url);
  if (e.request.method !== 'GET' || url.origin !== location.origin || url.pathname.startsWith('/api/')) return;
  // Pages: network first, fall back to the cached shell when offline.
  if (e.request.mode === 'navigate') {
    e.respondWith(fetch(e.request).catch(() => caches.match('/index.html')));
    return;
  }
  // Static files: cache first, then network.
  e.respondWith(caches.match(e.request).then((hit) => hit || fetch(e.request)));
});
