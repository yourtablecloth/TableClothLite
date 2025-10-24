/* Manifest version: GSTG7Bh1 */
// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    
    try {
        const cache = await caches.open(cacheName);
        await cache.addAll(assetsRequests);
    } catch (error) {
        console.error('Service worker installation failed:', error);
        // Try to cache assets individually if batch caching fails
        const cache = await caches.open(cacheName);
        for (const request of assetsRequests) {
            try {
                const response = await fetch(request);
                if (response.ok) {
                    await cache.put(request, response);
                }
            } catch (individualError) {
                console.error('Failed to cache:', request.url, individualError);
            }
        }
    }
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    let cachedResponse = null;
    
    try {
        const url = new URL(event.request.url);
        
        // Skip caching for localhost/development
        if (url.hostname === 'localhost' || url.hostname === '127.0.0.1') {
            try {
                return await fetch(event.request);
            } catch (error) {
                console.log('Development fetch failed:', error);
                return new Response('Development mode fetch failed', { status: 503 });
            }
        }
        
        // Skip caching for external domains
        if (url.origin !== self.location.origin) {
            try {
                return await fetch(event.request);
            } catch (error) {
                console.log('External fetch failed:', url.href, error);
                return new Response('External resource unavailable', { status: 503 });
            }
        }
        
        // Only handle GET requests
        if (event.request.method !== 'GET') {
            try {
                return await fetch(event.request);
            } catch (error) {
                console.log('Non-GET request failed:', event.request.method, url.href, error);
                return new Response('Request failed', { status: 503 });
            }
        }
        
        // For all navigation requests, try to serve index.html from cache,
        // unless that request is for an offline resource.
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && !manifestUrlList.some(manifestUrl => manifestUrl === event.request.url);

        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        
        try {
            const cache = await caches.open(cacheName);
            cachedResponse = await cache.match(request);
            
            if (cachedResponse) {
                return cachedResponse;
            }
            
            // Try to fetch from network
            try {
                const networkResponse = await fetch(event.request);
                
                if (networkResponse && networkResponse.ok) {
                    // Cache the new response for future use
                    if (shouldServeIndexHtml || manifestUrlList.includes(url.href)) {
                        cache.put(request, networkResponse.clone());
                    }
                    return networkResponse;
                } else {
                    console.log('Network response not ok:', networkResponse?.status, url.href);
                    return networkResponse || new Response('Network response not ok', { status: 502 });
                }
            } catch (fetchError) {
                console.log('Network fetch failed:', url.href, fetchError);
                
                // If navigation request fails and no cache, return error
                if (shouldServeIndexHtml) {
                    return new Response('Offline - No cached content available', {
                        status: 503,
                        statusText: 'Service Unavailable',
                        headers: { 'Content-Type': 'text/plain' }
                    });
                }
                
                return new Response('Network request failed and no cache available', {
                    status: 503,
                    statusText: 'Service Unavailable',
                    headers: { 'Content-Type': 'text/plain' }
                });
            }
        } catch (cacheError) {
            console.error('Cache operation error:', cacheError);
            
            // If cache operations fail, try direct fetch
            try {
                return await fetch(event.request);
            } catch (finalError) {
                console.error('Final fallback fetch failed:', finalError);
                return new Response('All fetch attempts failed', {
                    status: 503,
                    statusText: 'Service Unavailable',
                    headers: { 'Content-Type': 'text/plain' }
                });
            }
        }
    } catch (error) {
        // Top-level error handler for unexpected errors
        console.error('Unexpected error in onFetch:', error);
        return new Response('Unexpected service worker error', {
            status: 500,
            statusText: 'Internal Server Error',
            headers: { 'Content-Type': 'text/plain' }
        });
    }
}
