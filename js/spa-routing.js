// GitHub Pages SPA 라우팅 도우미
// 이 스크립트는 GitHub Pages에서 SPA 라우팅을 지원합니다.

(function() {
    'use strict';

    // GitHub Pages에서 404 에러 시 라우팅 처리
    function handleGitHubPagesRouting() {
        // 현재 URL이 GitHub Pages의 404 리디렉션인지 확인
        var pathSegmentsToKeep = 1; // GitHub Pages 프로젝트의 경우 1, 사용자 페이지의 경우 0
        var l = window.location;
        
        if (l.search) {
            var decoded = l.search.slice(1).split('&').map(function(s) { 
                return s.replace(/~and~/g, '&')
            }).join('?');
            
            window.history.replaceState(null, null,
                l.pathname.slice(0, -1) + decoded + l.hash
            );
        }
    }

    // Blazor 라우터가 로드되기 전에 URL 처리
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', handleGitHubPagesRouting);
    } else {
        handleGitHubPagesRouting();
    }

    // 404 페이지로 리디렉션하는 함수 (정적 호스팅용)
    window.redirectTo404 = function(path) {
        var pathSegmentsToKeep = 1;
        var l = window.location;
        l.replace(
            l.protocol + '//' + l.hostname + (l.port ? ':' + l.port : '') +
            l.pathname.split('/').slice(0, 1 + pathSegmentsToKeep).join('/') + 
            '/?/' + l.pathname.slice(1).split('/').slice(pathSegmentsToKeep).join('/').replace(/&/g, '~and~') +
            (l.search ? '&' + l.search.slice(1).replace(/&/g, '~and~') : '') +
            l.hash
        );
    };

    // 브라우저 히스토리 API를 통한 네비게이션 감지
    var originalPushState = history.pushState;
    var originalReplaceState = history.replaceState;

    history.pushState = function() {
        originalPushState.apply(history, arguments);
        handleRouteChange();
    };

    history.replaceState = function() {
        originalReplaceState.apply(history, arguments);
        handleRouteChange();
    };

    window.addEventListener('popstate', handleRouteChange);

    function handleRouteChange() {
        // 라우트 변경 시 필요한 처리가 있다면 여기에 추가
        console.log('Route changed to:', window.location.pathname);
    }

    // Service Worker와의 통신을 통한 오프라인 라우팅 지원
    if ('serviceWorker' in navigator) {
        navigator.serviceWorker.addEventListener('message', function(event) {
            if (event.data && event.data.type === 'ROUTE_NOT_FOUND') {
                // Service Worker에서 라우트를 찾을 수 없을 때의 처리
                console.log('Route not found in cache:', event.data.url);
            }
        });
    }
})();