# Service Worker "Failed to fetch" 오류 수정

## 문제 상황
앱 초기 시작 시점에 아래와 같은 오류가 빈발했습니다:
```
TypeError: Failed to fetch
    at onFetch (service-worker.js:204:43)
```

## 근본 원인

1. **부적절한 에러 처리**: `onFetch` 함수에서 네트워크 요청 실패 시 에러를 throw하여 Service Worker가 크래시
2. **Race Condition**: Service Worker 설치 중 일부 자산이 아직 캐시되지 않은 상태에서 fetch 요청 발생
3. **네트워크 실패 시 Fallback 부재**: 오프라인 상태나 네트워크 불안정 시 적절한 응답을 반환하지 못함
4. **외부 리소스 처리 미흡**: 외부 도메인 요청 시 에러 핸들링 부재

## 적용된 수정 사항

### 1. 포괄적인 Try-Catch 블록 추가
```javascript
async function onFetch(event) {
    try {
        // 모든 fetch 로직
    } catch (error) {
        // 최상위 에러 핸들러
        console.error('Unexpected error in onFetch:', error);
        return new Response('Unexpected service worker error', {
            status: 500,
            statusText: 'Internal Server Error',
            headers: { 'Content-Type': 'text/plain' }
        });
    }
}
```

### 2. 개발 환경 분리
```javascript
// 개발 환경에서는 네트워크 우선 (캐시 없음)
if (url.hostname === 'localhost' || url.hostname === '127.0.0.1') {
    try {
        return await fetch(event.request);
    } catch (error) {
        console.log('Development fetch failed:', error);
        return new Response('Development mode fetch failed', { status: 503 });
    }
}
```

### 3. 외부 도메인 요청 처리
```javascript
// 외부 도메인 요청은 직접 통과 (캐시하지 않음)
if (url.origin !== self.location.origin) {
    try {
        return await fetch(event.request);
    } catch (error) {
        console.log('External fetch failed:', url.href, error);
        return new Response('External resource unavailable', { status: 503 });
    }
}
```

### 4. 다층 Fallback 전략
```javascript
// 1. 캐시에서 먼저 확인
cachedResponse = await caches.match(event.request);
if (cachedResponse) {
    return cachedResponse;
}

// 2. 캐시에 없으면 네트워크에서 가져오기
try {
    const networkResponse = await fetch(event.request);
    if (networkResponse && networkResponse.ok) {
        return networkResponse;
    }
} catch (fetchError) {
    // 3. 유사한 캐시 확인 (예: /index.html)
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
```

### 5. 네비게이션 요청 개선
```javascript
// 네비게이션 요청 (HTML) - 네트워크 우선 전략
if (event.request.mode === 'navigate') {
    try {
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
```

### 6. version.json 특별 처리
```javascript
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
        // 빈 JSON 반환하여 앱 크래시 방지
        return new Response('{}', { 
            status: 200, 
            headers: { 'Content-Type': 'application/json' }
        });
    }
}
```

### 7. 설치 단계 에러 처리 개선
```javascript
async function onInstall(event) {
    console.info('Service worker: Install');
    
    try {
        const cache = await caches.open(cacheName);
        await cache.addAll(assetsRequests);
    } catch (error) {
        console.error('Service worker installation failed:', error);
        // 배치 캐싱 실패 시 개별 캐싱으로 재시도
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
```

## 기대 효과

1. **안정성 향상**: 모든 네트워크 오류가 적절히 처리되어 Service Worker 크래시 방지
2. **오프라인 지원**: 네트워크 실패 시에도 캐시된 콘텐츠로 앱 사용 가능
3. **초기 로딩 개선**: 앱 시작 시 발생하던 "Failed to fetch" 오류 해결
4. **개발 경험 개선**: 개발 환경에서는 캐시 없이 최신 파일 사용
5. **외부 리소스 안정성**: CDN이나 외부 API 장애 시에도 앱 정상 작동

## 테스트 방법

1. **정상 시나리오**
   - 앱을 새로 고침하여 Service Worker가 정상 설치되는지 확인
   - 네트워크 탭에서 리소스가 Service Worker에서 제공되는지 확인

2. **오프라인 시나리오**
   - 개발자 도구에서 "Offline" 모드 활성화
   - 앱이 캐시된 콘텐츠로 정상 작동하는지 확인

3. **네트워크 불안정 시나리오**
   - 개발자 도구에서 "Slow 3G" 등의 네트워크 제한 설정
   - 앱이 타임아웃이나 에러 없이 로딩되는지 확인

4. **초기 로딩 시나리오**
   - Application > Service Workers에서 "Unregister" 클릭
   - 페이지를 완전히 새로 고침하여 Service Worker 재설치
   - 콘솔에 "Failed to fetch" 오류가 발생하지 않는지 확인

## 변경된 파일

- `TableClothLite\wwwroot\service-worker.js` - 개발용 Service Worker
- `TableClothLite\wwwroot\service-worker.published.js` - 프로덕션용 Service Worker

## 추가 권장 사항

1. **모니터링 추가**: 프로덕션 환경에서 Service Worker 관련 오류를 추적할 수 있도록 에러 로깅 서비스(예: Sentry) 통합 고려
2. **캐시 정책 검토**: 리소스 유형별로 최적의 캐시 전략 수립
3. **업데이트 전략**: Service Worker 업데이트 시 사용자에게 부드러운 알림 제공 (이미 구현됨)
4. **테스트 자동화**: Cypress나 Playwright를 사용한 E2E 테스트에 Service Worker 시나리오 추가

## 참고 자료

- [Service Worker Lifecycle](https://developers.google.com/web/fundamentals/primers/service-workers/lifecycle)
- [Offline Cookbook](https://web.dev/offline-cookbook/)
- [Blazor PWA Guidelines](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app)
