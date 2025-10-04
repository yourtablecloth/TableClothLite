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

// beforeunload 이벤트 핸들러 관련 함수들
let dotNetHelperRef = null;
let beforeUnloadHandler = null;

// DotNet helper 참조 설정 및 beforeunload 핸들러 등록
window.setupBeforeUnloadHandler = function (dotNetHelper) {
    dotNetHelperRef = dotNetHelper;
    
    // 기존 핸들러가 있다면 제거
    if (beforeUnloadHandler) {
        window.removeEventListener('beforeunload', beforeUnloadHandler);
    }
    
    // 새로운 beforeunload 핸들러 등록
    beforeUnloadHandler = function (e) {
        try {
            // DotNet 메서드 호출하여 unsaved content 확인
            const hasUnsavedContent = dotNetHelperRef.invokeMethod('HasUnsavedContent');
            
            if (hasUnsavedContent) {
                // 표준 메시지 설정
                const message = '현재 진행 중인 대화 내용이 있습니다. 페이지를 떠나면 대화 내용이 사라집니다.';
                
                // Chrome 34+
                e.returnValue = message;
                
                // Safari, Firefox
                e.preventDefault();
                
                // 일부 구형 브라우저
                return message;
            }
        } catch (error) {
            console.warn('beforeunload 핸들러에서 오류 발생:', error);
        }
    };
    
    window.addEventListener('beforeunload', beforeUnloadHandler);
};

// beforeunload 핸들러 정리
window.cleanupBeforeUnloadHandler = function () {
    if (beforeUnloadHandler) {
        window.removeEventListener('beforeunload', beforeUnloadHandler);
        beforeUnloadHandler = null;
    }
    dotNetHelperRef = null;
};

// 페이지 네비게이션 시에도 확인 (SPA 라우팅용)
window.setupNavigationGuard = function () {
    // Blazor의 NavigationManager를 위한 추가 보호
    const originalPushState = history.pushState;
    const originalReplaceState = history.replaceState;
    
    history.pushState = function (...args) {
        if (dotNetHelperRef) {
            try {
                const hasUnsavedContent = dotNetHelperRef.invokeMethod('HasUnsavedContent');
                if (hasUnsavedContent) {
                    const shouldNavigate = confirm('현재 진행 중인 대화 내용이 있습니다. 페이지를 떠나면 대화 내용이 사라집니다. 계속하시겠습니까?');
                    if (!shouldNavigate) {
                        return;
                    }
                }
            } catch (error) {
                console.warn('Navigation guard에서 오류 발생:', error);
            }
        }
        originalPushState.apply(history, args);
    };
    
    history.replaceState = function (...args) {
        if (dotNetHelperRef) {
            try {
                const hasUnsavedContent = dotNetHelperRef.invokeMethod('HasUnsavedContent');
                if (hasUnsavedContent) {
                    const shouldNavigate = confirm('현재 진행 중인 대화 내용이 있습니다. 페이지를 떠나면 대화 내용이 사라집니다. 계속하시겠습니까?');
                    if (!shouldNavigate) {
                        return;
                    }
                }
            } catch (error) {
                console.warn('Navigation guard에서 오류 발생:', error);
            }
        }
        originalReplaceState.apply(history, args);
    };
};

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

        const isSwipeRight = diffX > 0 && this.startX < 20; // 화면 왼쪽 가장자리에
