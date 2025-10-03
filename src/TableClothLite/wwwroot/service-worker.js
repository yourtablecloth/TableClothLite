// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/, /version\.json$/ ]; // version.json 제외

// 빌드 정보 변수 - GitHub Actions에서 자동 업데이트
const APP_VERSION = '2024.1.0';
const BUILD_TIMESTAMP = 1234567890;

async function onInstall(event) {
    console.info('Service worker: Install');

    // 즉시 활성화 - 사용자 경험을 위해 비활성화
    // self.skipWaiting(); // 주석 - 즉시 활성화하지 않음

    // 스마트 캐시 - 해시 비교를 통한 선택적 캐시
    const cache = await caches.open(cacheName);
    const existingCache = await caches.open(cacheNamePrefix + 'previous');
    
    const assetsToCache = [];
    
    for (const asset of self.assetsManifest.assets) {
        if (offlineAssetsInclude.some(pattern => pattern.test(asset.url)) && 
            !offlineAssetsExclude.some(pattern => pattern.test(asset.url))) {
            
            // 기존 캐시된 응답과 해시 비교
            const cachedResponse = await existingCache.match(asset.url);
            const currentHash = asset.hash;
            
            let shouldCache = true;
            if (cachedResponse) {
                const cachedHash = cachedResponse.headers.get('x-asset-hash');
                if (cachedHash === currentHash) {
                    // 해시가 같으면 기존 캐시 재사용
                    const clonedResponse = cachedResponse.clone();
                    await cache.put(asset.url, clonedResponse);
                    shouldCache = false;
                    console.log(`캐시 재사용: ${asset.url}`);
                }
            }
            
            if (shouldCache) {
                // version.json과 같은 동적 파일은 SRI 체크 없이 캐시
                if (/version\.json$/.test(asset.url)) {
                    assetsToCache.push(new Request(asset.url, { 
                        cache: 'reload' 
                    }));
                } else {
                    assetsToCache.push(new Request(asset.url, { 
                        integrity: asset.hash, 
                        cache: 'reload' 
                    }));
                }
                console.log(`새로 캐시: ${asset.url}`);
            }
        }
    }
    
    // 필요한 자원만 다운로드
    if (assetsToCache.length > 0) {
        console.log(`${assetsToCache.length}개 자원을 새로 다운로드합니다.`);
        // 배치로 처리하여 에러 방지
        const batchSize = 10;
        for (let i = 0; i < assetsToCache.length; i += batchSize) {
            const batch = assetsToCache.slice(i, i + batchSize);
            try {
                await cache.addAll(batch);
                console.log(`배치 ${Math.floor(i/batchSize) + 1} 완료`);
            } catch (error) {
                console.error(`배치 ${Math.floor(i/batchSize) + 1} 실패:`, error);
                // 개별 요청으로 재시도
                for (const request of batch) {
                    try {
                        const response = await fetch(request);
                        if (response.ok) {
                            await cache.put(request, response);
                        }
                    } catch (individualError) {
                        console.error(`개별 요청 실패: ${request.url}`, individualError);
                    }
                }
            }
        }
    } else {
        console.log('모든 자원이 최신 상태입니다.');
    }
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // 클라이언트 즉시 제어 시작
    // await clients.claim(); // 주석 - 즉시 적용하지 않음

    // 이전 캐시를 'previous'로 보관 후 정리
    const cacheKeys = await caches.keys();
    const previousCaches = cacheKeys.filter(key => 
        key.startsWith(cacheNamePrefix) && key !== cacheName
    );
    
    if (previousCaches.length > 0) {
        // 가장 최신 캐시를 'previous'로 보관
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
        
        // 나머지 캐시들 삭제
        await Promise.all(previousCaches.map(key => caches.delete(key)));
    }
        
    // 클라이언트에게 조용한 업데이트 알림
    const allClients = await clients.matchAll();
    allClients.forEach(client => {
        client.postMessage({
            type: 'SW_UPDATED_QUIETLY',
            version: APP_VERSION,
            timestamp: BUILD_TIMESTAMP,
            message: '백그라운드에서 업데이트가 완료되었습니다.'
        });
    });
}

async function onFetch(event) {
    // 개발 환경에서는 네트워크 우선
    if (event.request.url.includes('localhost') || event.request.url.includes('127.0.0.1')) {
        return fetch(event.request);
    }
    
    // version.json은 항상 네트워크에서 가져오기 (캐시 무시)
    if (event.request.url.includes('version.json')) {
        try {
            return await fetch(event.request, {
                cache: 'no-cache',
                headers: {
                    'Cache-Control': 'no-cache, no-store, must-revalidate',
                    'Pragma': 'no-cache'
                }
            });
        } catch (error) {
            console.log('version.json fetch failed:', error);
            // 404 등의 에러는 조용히 처리
            return new Response('{}', { 
                status: 200, 
                headers: { 'Content-Type': 'application/json' }
            });
        }
    }
    
    // 네비게이션 요청 (HTML)
    if (event.request.mode === 'navigate') {
        try {
            // 네트워크 우선, 실패시 캐시
            const networkResponse = await fetch(event.request, {
                cache: 'no-cache'
            });
            
            if (networkResponse.ok) {
                return networkResponse;
            }
        } catch (error) {
            console.log('Network failed for navigation, falling back to cache');
        }
        
        // 네트워크 실패 시 캐시된 HTML 반환
        const cachedResponse = await caches.match('/');
        if (cachedResponse) {
            return cachedResponse;
        }
    }
    
    // 기타 리소스 요청
    if (event.request.method === 'GET') {
        const cachedResponse = await caches.match(event.request);
        
        if (cachedResponse) {
            // 캐시된 리소스 반환하면서 백그라운드에서 최신성 확인
            if (shouldCheckFreshness(event.request)) {
                event.waitUntil(checkAndUpdateCache(event.request));
            }
            return cachedResponse;
        }
        
        // 캐시에 없으면 네트워크에서 가져와서 캐시
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
    
    // 기타 요청은 네트워크로
    return fetch(event.request);
}

// 캐시 대상 리소스인지 확인
function shouldCache(request) {
    const url = new URL(request.url);
    return offlineAssetsInclude.some(pattern => pattern.test(url.pathname)) &&
           !offlineAssetsExclude.some(pattern => pattern.test(url.pathname));
}

// 최신성 확인이 필요한 리소스인지 판단
function shouldCheckFreshness(request) {
    // CSS, JS 파일만 주기적으로 최신성 확인
    return /\.(css|js)$/.test(request.url) && Math.random() < 0.1; // 10% 확률로 체크
}

// 백그라운드에서 캐시 최신성 확인 및 업데이트
async function checkAndUpdateCache(request) {
    try {
        const networkResponse = await fetch(request, { cache: 'no-cache' });
        const cachedResponse = await caches.match(request);
        
        if (networkResponse.ok && cachedResponse) {
            const networkETag = networkResponse.headers.get('etag');
            const networkLastModified = networkResponse.headers.get('last-modified');
            const cachedETag = cachedResponse.headers.get('etag');
            const cachedLastModified = cachedResponse.headers.get('last-modified');
            
            // ETag 또는 Last-Modified가 다르면 업데이트
            if ((networkETag && networkETag !== cachedETag) ||
                (networkLastModified && networkLastModified !== cachedLastModified)) {
                
                const cache = await caches.open(cacheName);
                await cache.put(request, networkResponse);
                console.log(`백그라운드 업데이트: ${request.url}`);
            }
        }
    } catch (error) {
        console.log('Background update failed:', error);
    }
}

// 메시지 리스너
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
