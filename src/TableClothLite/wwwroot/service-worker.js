// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));
self.addEventListener('message', event => onMessage(event));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/, /version\.json$/ ]; // version.json 제외

// 빌드 정보 변수 - GitHub Actions에서 자동 업데이트
const APP_VERSION = '2024.1.0';
const BUILD_TIMESTAMP = 1234567890;

// 오프라인 페이지 URL
const OFFLINE_PAGE = '/offline.html';

async function onInstall(event) {
    console.info('Service worker: Install');

    // 스마트 캐시 - 해시 비교를 통한 선택적 캐시
    const cache = await caches.open(cacheName);
    const existingCache = await caches.open(cacheNamePrefix + 'previous');
    
    const assetsToCache = [];
    
    // 오프라인 페이지 우선 캐시
    try {
        const offlineResponse = await fetch(OFFLINE_PAGE);
        if (offlineResponse.ok) {
            await cache.put(OFFLINE_PAGE, offlineResponse);
            console.log('오프라인 페이지 캐시됨');
        }
    } catch (error) {
        console.log('오프라인 페이지 캐시 실패:', error);
    }
    
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
                if (/version\.json$/.test(asset.url) || /\.webmanifest$/.test(asset.url)) {
                    assetsToCache.push(new Request(asset.url, { 
                        cache: 'reload' 
                    }));
                } else {
                    assetsToCache.push(new Request(asset.url, { 
                        integrity: asset.hash, 
                        cache: 'reload' 
                    }));
                }
            }
        }
    }
    
    // 배치 캐싱 - 성능 최적화
    const batchSize = 50;
    for (let i = 0; i < assetsToCache.length; i += batchSize) {
        const batch = assetsToCache.slice(i, i + batchSize);
        await Promise.allSettled(batch.map(async request => {
            try {
                const response = await fetch(request);
                if (response.ok) {
                    const responseToCache = response.clone();
                    
                    // 해시 정보를 헤더에 추가
                    const asset = self.assetsManifest.assets.find(a => 
                        request.url.endsWith(a.url)
                    );
                    
                    if (asset && asset.hash) {
                        const headers = new Headers(responseToCache.headers);
                        headers.set('x-asset-hash', asset.hash);
                        
                        const newResponse = new Response(responseToCache.body, {
                            status: responseToCache.status,
                            statusText: responseToCache.statusText,
                            headers: headers
                        });
                        
                        await cache.put(request, newResponse);
                    } else {
                        await cache.put(request, responseToCache);
                    }
                    
                    console.log(`캐시됨: ${request.url}`);
                }
            } catch (error) {
                console.log(`캐시 실패: ${request.url}`, error);
            }
        }));
    }
    
    console.log(`Service Worker 설치 완료: ${assetsToCache.length}개 파일 캐시`);
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
    let cachedResponse = null;
    
    try {
        const url = new URL(event.request.url);
        
        // 개발 환경에서는 네트워크 우선 (캐시 없음)
        if (url.hostname === 'localhost' || url.hostname === '127.0.0.1') {
            try {
                return await fetch(event.request);
            } catch (error) {
                console.log('Development fetch failed:', error);
                return new Response('Development mode fetch failed', { status: 503 });
            }
        }
        
        // version.json은 항상 네트워크에서 가져오기 (캐시 무시)
        if (url.pathname.includes('version.json')) {
            try {
                const networkResponse = await fetch(event.request, {
                    cache: 'no-cache',
                    headers: {
                        'Cache-Control': 'no-cache, no-store, must-revalidate',
                        'Pragma': 'no-cache'
                    }
                });
                return networkResponse;
            } catch (error) {
                console.log('version.json fetch failed (expected during initial load):', error);
                // 404 등의 에러는 조용히 처리 - 빈 JSON 반환
                return new Response('{}', { 
                    status: 200, 
                    headers: { 'Content-Type': 'application/json' }
                });
            }
        }
        
        // 외부 도메인 요청은 직접 통과 (캐시하지 않음)
        if (url.origin !== self.location.origin) {
            try {
                return await fetch(event.request);
            } catch (error) {
                console.log('External fetch failed:', url.href, error);
                return new Response('External resource unavailable', { status: 503 });
            }
        }
        
        // GET 요청만 캐시 처리
        if (event.request.method !== 'GET') {
            try {
                return await fetch(event.request);
            } catch (error) {
                console.log('Non-GET request failed:', event.request.method, url.href, error);
                return new Response('Request failed', { status: 503 });
            }
        }
        
        // 네비게이션 요청 (HTML) - 네트워크 우선 전략
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
            cachedResponse = await caches.match('/index.html');
            if (cachedResponse) {
                return cachedResponse;
            }
            
            // 캐시도 없으면 offline 페이지 반환
            return new Response('Offline - No cached content available', { 
                status: 503,
                statusText: 'Service Unavailable',
                headers: { 'Content-Type': 'text/plain' }
            });
        }
        
        // 기타 리소스 요청 - 캐시 우선 전략
        try {
            // 1. 캐시에서 먼저 확인
            cachedResponse = await caches.match(event.request);
            
            if (cachedResponse) {
                // 캐시된 리소스 반환하면서 백그라운드에서 최신성 확인
                if (shouldCheckFreshness(event.request)) {
                    event.waitUntil(checkAndUpdateCache(event.request));
                }
                return cachedResponse;
            }
            
            // 2. 캐시에 없으면 네트워크에서 가져오기
            try {
                const networkResponse = await fetch(event.request);
                
                if (networkResponse && networkResponse.ok) {
                    // 캐시 가능한 리소스면 캐시에 저장
                    if (shouldCache(event.request)) {
                        const cache = await caches.open(cacheName);
                        cache.put(event.request, networkResponse.clone());
                    }
                    return networkResponse;
                } else {
                    // 응답이 성공적이지 않으면 에러 처리
                    console.log('Network response not ok:', networkResponse?.status, url.href);
                    return networkResponse || new Response('Network response not ok', { status: 502 });
                }
            } catch (fetchError) {
                console.log('Network fetch failed:', url.href, fetchError);
                
                // 네트워크 실패 시 fallback
                // 3. 유사한 캐시가 있는지 확인 (예: /index.html)
                if (url.pathname === '/' || url.pathname === '') {
                    const indexCache = await caches.match('/index.html');
                    if (indexCache) {
                        return indexCache;
                    }
                }
                
                // 4. 최종 fallback - 적절한 에러 응답 반환
                return new Response('Network request failed and no cache available', {
                    status: 503,
                    statusText: 'Service Unavailable',
                    headers: { 'Content-Type': 'text/plain' }
                });
            }
        } catch (cacheError) {
            console.error('Cache operation error:', cacheError);
            
            // 캐시 작업 자체가 실패한 경우 - 네트워크로 시도
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
        // 최상위 에러 핸들러 - 예상치 못한 오류
        console.error('Unexpected error in onFetch:', error);
        return new Response('Unexpected service worker error', {
            status: 500,
            statusText: 'Internal Server Error',
            headers: { 'Content-Type': 'text/plain' }
        });
    }
}

// 캐시 대상 리소스인지 확인
function shouldCache(request) {
    try {
        const url = new URL(request.url);
        return offlineAssetsInclude.some(pattern => pattern.test(url.pathname)) &&
               !offlineAssetsExclude.some(pattern => pattern.test(url.pathname));
    } catch (error) {
        console.error('Error in shouldCache:', error);
        return false;
    }
}

// 최신성 확인이 필요한 리소스인지 판단
function shouldCheckFreshness(request) {
    try {
        // CSS, JS 파일만 주기적으로 최신성 확인
        return /\.(css|js)$/.test(request.url) && Math.random() < 0.1; // 10% 확률로 체크
    } catch (error) {
        console.error('Error in shouldCheckFreshness:', error);
        return false;
    }
}

// 백그라운드에서 캐시 최신성 확인 및 업데이트
async function checkAndUpdateCache(request) {
    try {
        const networkResponse = await fetch(request, { cache: 'no-cache' });
        const cachedResponse = await caches.match(request);
        
        if (networkResponse && networkResponse.ok && cachedResponse) {
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
        // 백그라운드 업데이트 실패는 무시 (중요하지 않음)
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
        }).catch(error => {
            console.error('Error checking cache status:', error);
        });
    }
});
