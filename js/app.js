class Helpers {
    static dotNetHelper;

    static setDotNetHelper(value) {
        Helpers.dotNetHelper = value;
    }

    static async openSandbox(url) {
        await Helpers.dotNetHelper.invokeMethodAsync('OpenSandbox', url);
    }
}

window.Helpers = Helpers;

window.scrollToBottom = function (elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
};

// 부드러운 스크롤 함수
window.smoothScrollToBottom = function (elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.scrollTo({
            top: element.scrollHeight,
            behavior: 'smooth'
        });
    }
};

// 텍스트 영역 자동 리사이즈 함수
window.autoResizeTextarea = function (elementId) {
    const textarea = document.getElementById(elementId);
    if (textarea) {
        textarea.style.height = 'auto';
        const scrollHeight = textarea.scrollHeight;
        const maxHeight = 120; // 최대 높이 설정
        textarea.style.height = Math.min(scrollHeight, maxHeight) + 'px';
    }
};

// 모바일 터치 제스처 처리
class MobileTouchHandler {
    constructor() {
        this.startX = 0;
        this.currentX = 0;
        this.isDragging = false;
        this.threshold = 50; // 스와이프 감지 임계값
    }

    init() {
        if (!this.isMobile()) return;

        const chatApp = document.querySelector('.chat-app');
        if (chatApp) {
            chatApp.addEventListener('touchstart', this.handleTouchStart.bind(this), { passive: true });
            chatApp.addEventListener('touchmove', this.handleTouchMove.bind(this), { passive: false });
            chatApp.addEventListener('touchend', this.handleTouchEnd.bind(this), { passive: true });
        }
    }

    isMobile() {
        return window.innerWidth <= 768;
    }

    handleTouchStart(e) {
        if (!this.isMobile()) return;
        
        const touch = e.touches[0];
        this.startX = touch.clientX;
        this.currentX = touch.clientX;
        this.isDragging = false;
    }

    handleTouchMove(e) {
        if (!this.isMobile()) return;

        const touch = e.touches[0];
        this.currentX = touch.clientX;
        const diffX = this.currentX - this.startX;
        
        const sidebar = document.querySelector('.sidebar');
        const overlay = document.querySelector('.mobile-overlay');
        
        if (!sidebar) return;

        const isSwipeRight = diffX > 0 && this.startX < 20; // 화면 왼쪽 가장자리에서 시작
        const isSidebarOpen = sidebar.classList.contains('open');
        const isSwipeLeft = diffX < 0 && isSidebarOpen;

        if (isSwipeRight || isSwipeLeft) {
            this.isDragging = true;
            e.preventDefault();
        }
    }

    handleTouchEnd(e) {
        if (!this.isMobile() || !this.isDragging) return;

        const diffX = this.currentX - this.startX;
        const sidebar = document.querySelector('.sidebar');
        
        if (!sidebar) return;

        const isSidebarOpen = sidebar.classList.contains('open');
        
        // 오른쪽 스와이프로 사이드바 열기
        if (diffX > this.threshold && this.startX < 20 && !isSidebarOpen) {
            this.toggleSidebar(true);
        }
        // 왼쪽 스와이프로 사이드바 닫기
        else if (diffX < -this.threshold && isSidebarOpen) {
            this.toggleSidebar(false);
        }

        this.isDragging = false;
    }

    toggleSidebar(open) {
        if (Helpers.dotNetHelper) {
            // CSS 클래스로 제어
            const sidebar = document.querySelector('.sidebar');
            const overlay = document.querySelector('.mobile-overlay');
            
            if (open) {
                sidebar?.classList.add('open');
                overlay?.classList.add('open');
            } else {
                sidebar?.classList.remove('open');
                overlay?.classList.remove('open');
            }
        }
    }
}

// 모바일 뷰포트 높이 조정 (iOS Safari 주소창 대응)
function setMobileViewportHeight() {
    const vh = window.innerHeight * 0.01;
    document.documentElement.style.setProperty('--vh', `${vh}px`);
}

// 모바일 키보드 대응
function handleMobileKeyboard() {
    if (!window.matchMedia('(max-width: 768px)').matches) return;

    const chatInput = document.getElementById('chatTextArea');
    if (!chatInput) return;

    chatInput.addEventListener('focus', function() {
        // iOS에서 키보드가 올라올 때 스크롤 위치 조정
        setTimeout(() => {
            const messagesContainer = document.getElementById('messages');
            if (messagesContainer) {
                messagesContainer.scrollTop = messagesContainer.scrollHeight;
            }
        }, 300);
    });

    // 키보드가 내려갈 때 뷰포트 높이 재조정
    window.addEventListener('resize', () => {
        setMobileViewportHeight();
    });
}

// 스마트 새로고침 함수 - 선택적 캐시 클리어
window.forceRefresh = function() {
    console.log('스마트 새로고침 시작...');
    
    // Service Worker에게 즉시 활성화 요청
    if ('serviceWorker' in navigator) {
        navigator.serviceWorker.getRegistrations().then(function(registrations) {
            for(let registration of registrations) {
                if (registration.waiting) {
                    registration.waiting.postMessage({ type: 'SKIP_WAITING' });
                }
                registration.update();
            }
        });
    }
    
    // 앱 버전 정보만 클리어 (사용자 데이터는 보존)
    const preserveKeys = ['openRouterApiKey', 'tablecloth-settings'];
    const preservedData = {};
    
    preserveKeys.forEach(key => {
        const value = localStorage.getItem(key);
        if (value) preservedData[key] = value;
    });
    
    // 앱 관련 캐시만 클리어
    Object.keys(localStorage).forEach(key => {
        if (key.startsWith('app-') || key.startsWith('hash_') || key === 'tablecloth-version') {
            localStorage.removeItem(key);
        }
    });
    
    // 보존된 데이터 복원
    Object.keys(preservedData).forEach(key => {
        localStorage.setItem(key, preservedData[key]);
    });
    
    // 부드러운 새로고침
    const timestamp = new Date().getTime();
    window.location.href = window.location.pathname + '?refresh=' + timestamp;
};

// 효율적인 업데이트 확인
window.checkForUpdates = async function() {
    try {
        const timestamp = new Date().getTime();
        const response = await fetch(`/version.json?t=${timestamp}`, { 
            cache: 'no-cache',
            headers: {
                'Cache-Control': 'no-cache, no-store, must-revalidate',
                'Pragma': 'no-cache'
            }
        });
        
        if (response.ok) {
            const serverInfo = await response.json();
            const storedVersion = localStorage.getItem('app-version');
            
            if (storedVersion && serverInfo.version && storedVersion !== serverInfo.version) {
                console.log('새 버전 감지:', {
                    current: storedVersion,
                    server: serverInfo.version,
                    commit: serverInfo.commit?.substring(0, 7)
                });
                
                showSmartUpdateNotification(serverInfo);
                return true;
            }
            
            if (serverInfo.version) {
                localStorage.setItem('app-version', serverInfo.version);
            }
        }
        return false;
    } catch (error) {
        console.log('업데이트 확인 실패:', error);
        return false;
    }
};

// 스마트 업데이트 알림
function showSmartUpdateNotification(serverInfo) {
    const currentVersion = localStorage.getItem('app-version');
    const newVersion = serverInfo.version;
    
    // 더 상세하고 친화적인 메시지
    const message = 
        `🎉 새 버전이 있습니다!\n\n` +
        `현재: ${currentVersion}\n` +
        `최신: ${newVersion}\n\n` +
        `✨ 새로운 기능과 개선사항이 포함되어 있습니다.\n` +
        `📱 변경된 파일만 다운로드하여 빠르게 업데이트됩니다.\n` +
        `💾 설정과 데이터는 안전하게 보존됩니다.\n\n` +
        `지금 업데이트하시겠습니까?`;
        
    if (confirm(message)) {
        window.forceRefresh();
    } else {
        // 나중에 알림 (1시간 후)
        setTimeout(() => {
            if (confirm('새 버전 업데이트를 건너뛰셨습니다.\n더 나은 경험을 위해 업데이트를 권장합니다.\n\n지금 업데이트하시겠습니까?')) {
                window.forceRefresh();
            }
        }, 60 * 60 * 1000);
    }
}

// 리소스 캐시 상태 확인
window.getCacheStatus = function() {
    if ('serviceWorker' in navigator && navigator.serviceWorker.controller) {
        const messageChannel = new MessageChannel();
        
        messageChannel.port1.onmessage = function(event) {
            if (event.data.type === 'CACHE_STATUS') {
                console.log('캐시 상태:', {
                    cachedFiles: event.data.cachedFiles,
                    version: event.data.version
                });
            }
        };
        
        navigator.serviceWorker.controller.postMessage(
            { type: 'CHECK_CACHE_STATUS' }, 
            [messageChannel.port2]
        );
    }
};

// 선택적 캐시 버스터 - 개발 환경에서만 사용
window.addCacheBuster = function(url) {
    // 프로덕션에서는 서비스 워커가 처리하므로 캐시 버스터 불필요
    if (window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1') {
        const timestamp = new Date().getTime();
        const buildVersion = '2024.1.0'; // GitHub Actions에서 자동 업데이트
        const separator = url.includes('?') ? '&' : '?';
        return url + separator + 'v=' + buildVersion + '&t=' + timestamp;
    }
    return url;
};

// Service Worker 메시지 리스너
if ('serviceWorker' in navigator) {
    navigator.serviceWorker.addEventListener('message', event => {
        if (event.data.type === 'SW_UPDATED_QUIETLY') {
            console.log('Service Worker 업데이트됨:', event.data);
            
            // 5분 후 부드러운 알림
            setTimeout(() => {
                const message = 
                    `🔄 백그라운드 업데이트 완료\n\n` +
                    `앱이 조용히 업데이트되었습니다.\n` +
                    `최신 기능을 사용하려면 새로고침을 권장합니다.\n\n` +
                    `지금 새로고침하시겠습니까?`;
                    
                if (confirm(message)) {
                    window.location.reload();
                }
            }, 5 * 60 * 1000);
        }
    });
}

// DOM 로드 후 최적화된 초기화
document.addEventListener('DOMContentLoaded', function() {
    // 캐시 상태 로깅 (개발용)
    if (window.location.hostname === 'localhost') {
        setTimeout(() => window.getCacheStatus(), 2000);
    }
    
    // 초기 버전 체크 (페이지 로드 후 10초)
    setTimeout(() => {
        window.checkForUpdates();
    }, 10000);
});

// 페이지 가시성 변경 시 효율적인 업데이트 체크
let lastVisibilityCheck = Date.now();
document.addEventListener('visibilitychange', function() {
    if (!document.hidden && Date.now() - lastVisibilityCheck > 600000) { // 10분 이상 경과
        lastVisibilityCheck = Date.now();
        console.log('페이지 활성화 - 업데이트 확인 중...');
        window.checkForUpdates();
    }
});

// 창 크기 가져오기 함수
window.getWindowWidth = function() {
    return window.innerWidth;
};

window.getWindowHeight = function() {
    return window.innerHeight;
};

// 네트워크 상태 모니터링
if ('connection' in navigator) {
    navigator.connection.addEventListener('change', function() {
        if (navigator.connection.effectiveType === '4g') {
            // 빠른 네트워크에서는 적극적으로 업데이트 체크
            setTimeout(() => window.checkForUpdates(), 1000);
        }
    });
}

// 개발용 디버그 함수들
if (window.location.hostname === 'localhost') {
    window.debugCache = {
        clearAll: () => {
            localStorage.clear();
            if ('serviceWorker' in navigator) {
                navigator.serviceWorker.getRegistrations().then(registrations => {
                    registrations.forEach(registration => registration.unregister());
                });
            }
            if ('caches' in window) {
                caches.keys().then(names => {
                    names.forEach(name => caches.delete(name));
                });
            }
            console.log('모든 캐시 클리어됨');
        },
        forceUpdate: () => window.forceRefresh(),
        checkVersion: () => window.checkForUpdates(),
        cacheStatus: () => window.getCacheStatus()
    };
    
    console.log('개발 모드 - 사용 가능한 디버그 함수:', Object.keys(window.debugCache));
}

// 채팅 입력 초기화 함수
window.initChatInput = function () {
    const textarea = document.getElementById('chatTextArea');
    if (textarea) {
        // 초기 높이 설정
        textarea.style.height = '24px';
        
        // input 이벤트 리스너 추가 (자동 리사이즈)
        textarea.addEventListener('input', function() {
            this.style.height = 'auto';
            const scrollHeight = this.scrollHeight;
            const maxHeight = 120;
            this.style.height = Math.min(scrollHeight, maxHeight) + 'px';
        });
        
        // 포커스 시 스크롤 방지
        textarea.addEventListener('focus', function() {
            setTimeout(() => {
                this.scrollTop = 0;
            }, 0);
        });
    }

    // 모바일 기능 초기화
    const touchHandler = new MobileTouchHandler();
    touchHandler.init();
    
    setMobileViewportHeight();
    handleMobileKeyboard();
    setupWindowResizeListener();

    // 페이지 포커스 이벤트 리스너 추가
    let isPageVisible = true;
    
    window.addEventListener('focus', async function() {
        if (!isPageVisible) {
            isPageVisible = true;
            // 페이지가 포커스를 받았을 때 API 키 상태 재확인
            if (Helpers.dotNetHelper) {
                try {
                    await Helpers.dotNetHelper.invokeMethodAsync('OnPageFocus');
                } catch (error) {
                    console.log('페이지 포커스 핸들링 중 오류:', error);
                }
            }
        }
    });
    
    window.addEventListener('blur', function() {
        isPageVisible = false;
    });

    // Visibility API를 사용한 추가적인 감지
    document.addEventListener('visibilitychange', async function() {
        if (!document.hidden && !isPageVisible) {
            isPageVisible = true;
            if (Helpers.dotNetHelper) {
                try {
                    await Helpers.dotNetHelper.invokeMethodAsync('OnPageFocus');
                } catch (error) {
                    console.log('Visibility change 핸들링 중 오류:', error);
                }
            }
        } else if (document.hidden) {
            isPageVisible = false;
        }
    });
};

// 창 크기 변경 리스너 설정
function setupWindowResizeListener() {
    let resizeTimeout;
    
    window.addEventListener('resize', function() {
        clearTimeout(resizeTimeout);
        resizeTimeout = setTimeout(async function() {
            const width = window.innerWidth;
            
            // Blazor 컴포넌트에 창 크기 변경 알림
            if (Helpers.dotNetHelper) {
                try {
                    await Helpers.dotNetHelper.invokeMethodAsync('OnWindowResize', width);
                } catch (error) {
                    console.log('창 크기 변경 핸들링 중 오류:', error);
                }
            }
            
            // 뷰포트 높이 재조정
            setMobileViewportHeight();
        }, 100); // 100ms 디바운싱
    });
}

// 다크 모드 토글 함수
window.toggleDarkMode = function () {
    const body = document.body;
    const isDark = body.getAttribute('data-theme') === 'dark';
    
    if (isDark) {
        body.removeAttribute('data-theme');
        localStorage.setItem('theme', 'light');
    } else {
        body.setAttribute('data-theme', 'dark');
        localStorage.setItem('theme', 'dark');
    }
};

// 테마 초기화 함수
window.initTheme = function () {
    const savedTheme = localStorage.getItem('theme');
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    
    if (savedTheme === 'dark' || (!savedTheme && prefersDark)) {
        document.body.setAttribute('data-theme', 'dark');
    }
};

// 페이지 로드 시 테마 초기화
document.addEventListener('DOMContentLoaded', function() {
    window.initTheme();
    setMobileViewportHeight();
});

// 뷰포트 높이 변경 감지 (키보드, 회전 등)
window.addEventListener('resize', () => {
    setMobileViewportHeight();
});

window.addEventListener('orientationchange', () => {
    setTimeout(() => {
        setMobileViewportHeight();
    }, 100);
});

// 미디어 쿼리 변경 감지
window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function(e) {
    const savedTheme = localStorage.getItem('theme');
    if (!savedTheme) {
        if (e.matches) {
            document.body.setAttribute('data-theme', 'dark');
        } else {
            document.body.removeAttribute('data-theme');
        }
    }
});

// 모바일에서 더블 탭 줌 방지
document.addEventListener('touchend', function(e) {
    const now = new Date().getTime();
    const timesince = now - window.lastTouchEnd;
    
    if (timesince < 300 && timesince > 0) {
        e.preventDefault();
    }
    
    window.lastTouchEnd = now;
}, false);

window.downloadFileStream = async (fileName, contentType, dotNetStreamReference) => {
    const stream = await dotNetStreamReference.stream();
    const reader = stream.getReader();
    const chunks = [];

    // 데이터를 청크 단위로 읽기
    while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        chunks.push(value);
    }

    // Blob 생성
    const blob = new Blob(chunks, { type: contentType });
    const url = URL.createObjectURL(blob);

    // 다운로드 처리
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);

    // 메모리 정리
    URL.revokeObjectURL(url);
};

// 복사 기능
window.copyToClipboard = async function (text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch (err) {
        // 대체 방법
        const textarea = document.createElement('textarea');
        textarea.value = text;
        document.body.appendChild(textarea);
        textarea.select();
        const success = document.execCommand('copy');
        document.body.removeChild(textarea);
        return success;
    }
};

// 모바일 최적화: 스크롤 관성 개선
window.optimizeScrolling = function() {
    const messagesContainer = document.getElementById('messages');
    if (messagesContainer) {
        messagesContainer.style.webkitOverflowScrolling = 'touch';
    }
};

// PWA 관련: 설치 프롬프트 처리
let deferredPrompt;
window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault();
    deferredPrompt = e;
});

window.showInstallPrompt = function() {
    if (deferredPrompt) {
        deferredPrompt.prompt();
        deferredPrompt.userChoice.then((choiceResult) => {
            deferredPrompt = null;
        });
    }
};
