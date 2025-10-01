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

// �� ���� ���� - GitHub Actions���� �ڵ� ������Ʈ
const APP_VERSION = '2024.1.0';
const BUILD_TIMESTAMP = 1234567890;

async function onInstall(event) {
    console.info('Service worker: Install');

    // ������ Ȱ��ȭ - ����� ����¡ �������� ����
    // self.skipWaiting(); // ���� - ��� Ȱ��ȭ���� ����

    // ����Ʈ ĳ�� - �ؽ� ������� ����� ���¸� ĳ��
    const cache = await caches.open(cacheName);
    const existingCache = await caches.open(cacheNamePrefix + 'previous');
    
    const assetsToCache = [];
    
    for (const asset of self.assetsManifest.assets) {
        if (offlineAssetsInclude.some(pattern => pattern.test(asset.url)) && 
            !offlineAssetsExclude.some(pattern => pattern.test(asset.url))) {
            
            // ���� ĳ�õ� ���°� �ؽ� ��
            const cachedResponse = await existingCache.match(asset.url);
            const currentHash = asset.hash;
            
            let shouldCache = true;
            if (cachedResponse) {
                const cachedHash = cachedResponse.headers.get('x-asset-hash');
                if (cachedHash === currentHash) {
                    // �ؽð� ������ ���� ĳ�� ����
                    const clonedResponse = cachedResponse.clone();
                    await cache.put(asset.url, clonedResponse);
                    shouldCache = false;
                    console.log(`ĳ�� ����: ${asset.url}`);
                }
            }
            
            if (shouldCache) {
                assetsToCache.push(new Request(asset.url, { 
                    integrity: asset.hash, 
                    cache: 'reload' 
                }));
                console.log(`���� ĳ��: ${asset.url}`);
            }
        }
    }
    
    // ����� ���¸� �ٿ�ε�
    if (assetsToCache.length > 0) {
        console.log(`${assetsToCache.length}�� ������ ���� �ٿ�ε��մϴ�.`);
        await cache.addAll(assetsToCache);
    } else {
        console.log('��� ������ �ֽ� �����Դϴ�.');
    }
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Ŭ���̾�Ʈ ����� ���� �湮�ú���
    // await clients.claim(); // ���� - ��� �������� ����

    // ���� ĳ�ø� 'previous'�� ��� �� ����
    const cacheKeys = await caches.keys();
    const previousCaches = cacheKeys.filter(key => 
        key.startsWith(cacheNamePrefix) && key !== cacheName
    );
    
    if (previousCaches.length > 0) {
        // ���� �ֽ� ĳ�ø� 'previous'�� ����
        const latestPreviousCache = previousCaches.sort().pop();
        if (latestPreviousCache) {
            const oldCache = await caches.open(latestPreviousCache);
            const newPreviousCache = await caches.open(cacheNamePrefix + 'previous');
            
            const oldRequests = await oldCache.keys();
            for (const request of oldRequests) {
                const response = await oldCache.match(request);
                if (response) {
                    await newPreviousCache.put(request, response);
                }
            }
        }
        
        // ������ ĳ�õ� ����
        await Promise.all(previousCaches.map(key => caches.delete(key)));
    }
        
    // Ŭ���̾�Ʈ���� �ε巯�� ������Ʈ �˸�
    const allClients = await clients.matchAll();
    allClients.forEach(client => {
        client.postMessage({
            type: 'SW_UPDATED_QUIETLY',
            version: APP_VERSION,
            timestamp: BUILD_TIMESTAMP,
            message: '��׶��忡�� ������Ʈ�� �Ϸ�Ǿ����ϴ�.'
        });
    });
}

async function onFetch(event) {
    // ���� ȯ�濡���� ��Ʈ��ũ �켱
    if (event.request.url.includes('localhost') || event.request.url.includes('127.0.0.1')) {
        return fetch(event.request);
    }
    
    // �׺���̼� ��û (HTML)
    if (event.request.mode === 'navigate') {
        try {
            // ��Ʈ��ũ �켱, ���н� ĳ��
            const networkResponse = await fetch(event.request, {
                cache: 'no-cache'
            });
            
            if (networkResponse.ok) {
                return networkResponse;
            }
        } catch (error) {
            console.log('Network failed for navigation, falling back to cache');
        }
        
        // ��Ʈ��ũ ���� �� ĳ�õ� HTML ��ȯ
        const cachedResponse = await caches.match('/');
        if (cachedResponse) {
            return cachedResponse;
        }
    }
    
    // ���� ���ҽ� ��û
    if (event.request.method === 'GET') {
        const cachedResponse = await caches.match(event.request);
        
        if (cachedResponse) {
            // ĳ�õ� ���ҽ� ��ȯ�ϸ鼭 ��׶��忡�� �ֽż� Ȯ��
            if (shouldCheckFreshness(event.request)) {
                event.waitUntil(checkAndUpdateCache(event.request));
            }
            return cachedResponse;
        }
        
        // ĳ�ÿ� ������ ��Ʈ��ũ���� �����ͼ� ĳ��
        try {
            const networkResponse = await fetch(event.request);
            
            if (networkResponse.ok && shouldCache(event.request)) {
                const cache = await caches.open(cacheName);
                cache.put(event.request, networkResponse.clone());
            }
            
            return networkResponse;
        } catch (error) {
            console.error('Network request failed:', error);
            throw error;
        }
    }
    
    // ��Ÿ ��û�� ��Ʈ��ũ��
    return fetch(event.request);
}

// ĳ�� ������ ���ҽ����� Ȯ��
function shouldCache(request) {
    const url = new URL(request.url);
    return offlineAssetsInclude.some(pattern => pattern.test(url.pathname)) &&
           !offlineAssetsExclude.some(pattern => pattern.test(url.pathname));
}

// �ֽż� Ȯ���� �ʿ��� ���ҽ����� �Ǵ�
function shouldCheckFreshness(request) {
    // CSS, JS ������ �ֱ������� �ֽż� Ȯ��
    return /\.(css|js)$/.test(request.url) && Math.random() < 0.1; // 10% Ȯ���� üũ
}

// ��׶��忡�� ĳ�� �ֽż� Ȯ�� �� ������Ʈ
async function checkAndUpdateCache(request) {
    try {
        const networkResponse = await fetch(request, { cache: 'no-cache' });
        const cachedResponse = await caches.match(request);
        
        if (networkResponse.ok && cachedResponse) {
            const networkETag = networkResponse.headers.get('etag');
            const networkLastModified = networkResponse.headers.get('last-modified');
            const cachedETag = cachedResponse.headers.get('etag');
            const cachedLastModified = cachedResponse.headers.get('last-modified');
            
            // ETag �Ǵ� Last-Modified�� �ٸ��� ������Ʈ
            if ((networkETag && networkETag !== cachedETag) ||
                (networkLastModified && networkLastModified !== cachedLastModified)) {
                
                const cache = await caches.open(cacheName);
                await cache.put(request, networkResponse);
                console.log(`��׶��� ������Ʈ: ${request.url}`);
            }
        }
    } catch (error) {
        console.log('Background update failed:', error);
    }
}

// �޽��� ������
self.addEventListener('message', event => {
    if (event.data && event.data.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
    
    if (event.data && event.data.type === 'GET_VERSION') {
        event.ports[0].postMessage({
            version: APP_VERSION,
            timestamp: BUILD_TIMESTAMP
        });
    }
    
    if (event.data && event.data.type === 'CHECK_CACHE_STATUS') {
        caches.open(cacheName).then(cache => {
            cache.keys().then(keys => {
                event.ports[0].postMessage({
                    type: 'CACHE_STATUS',
                    cachedFiles: keys.length,
                    version: APP_VERSION
                });
            });
        });
    }
});
