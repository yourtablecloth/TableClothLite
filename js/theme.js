// 테마 관리 JavaScript 모듈
let themeDetectionCallback = null;
let mediaQueryList = null;

// 시스템 다크모드 상태 감지 (확장된 정보 포함)
export function getSystemDarkMode() {
    if (!window.matchMedia) {
        console.warn('Media Query 지원 안함 - 기본값 false 반환');
        return false;
    }
    
    const darkModeQuery = window.matchMedia('(prefers-color-scheme: dark)');
    const lightModeQuery = window.matchMedia('(prefers-color-scheme: light)');
    const noPreferenceQuery = window.matchMedia('(prefers-color-scheme: no-preference)');
    
    console.log('시스템 테마 감지 정보:', {
        supportsDarkMode: darkModeQuery.matches,
        supportsLightMode: lightModeQuery.matches,
        noPreference: noPreferenceQuery.matches,
        userAgent: navigator.userAgent,
        currentTime: new Date().toLocaleString()
    });
    
    return darkModeQuery.matches;
}

// 시스템 테마 정보를 가져오는 새로운 함수
export function getSystemThemeInfo() {
    if (!window.matchMedia) {
        return {
            supported: false,
            current: 'unknown',
            reason: 'Media Query not supported'
        };
    }
    
    const darkQuery = window.matchMedia('(prefers-color-scheme: dark)');
    const lightQuery = window.matchMedia('(prefers-color-scheme: light)');
    
    let current = 'no-preference';
    let reason = '시스템에서 선호도를 설정하지 않음';
    
    if (darkQuery.matches) {
        current = 'dark';
        reason = '시스템에서 다크 모드를 선호함';
    } else if (lightQuery.matches) {
        current = 'light';
        reason = '시스템에서 라이트 모드를 선호함';
    }
    
    return {
        supported: true,
        current: current,
        reason: reason,
        timestamp: new Date().toISOString(),
        platform: getPlatformInfo()
    };
}

// 플랫폼 정보 감지
function getPlatformInfo() {
    const ua = navigator.userAgent;
    
    if (ua.includes('Windows')) return 'Windows';
    if (ua.includes('Mac')) return 'macOS';
    if (ua.includes('iPhone') || ua.includes('iPad')) return 'iOS';
    if (ua.includes('Android')) return 'Android';
    if (ua.includes('Linux')) return 'Linux';
    
    return 'Unknown';
}

// 테마 감지 초기화
export function initializeThemeDetection(dotNetObjectRef) {
    themeDetectionCallback = dotNetObjectRef;
    
    if (window.matchMedia) {
        mediaQueryList = window.matchMedia('(prefers-color-scheme: dark)');
        
        // 이벤트 리스너 등록
        const handleChange = (e) => {
            if (themeDetectionCallback) {
                themeDetectionCallback.invokeMethodAsync('OnSystemThemeChanged', e.matches);
            }
        };
        
        // 최신 브라우저용
        if (mediaQueryList.addEventListener) {
            mediaQueryList.addEventListener('change', handleChange);
        } 
        // 구형 브라우저용
        else if (mediaQueryList.addListener) {
            mediaQueryList.addListener(handleChange);
        }
        
        // 리스너 제거 함수를 저장
        mediaQueryList._themeChangeHandler = handleChange;
    }
}

// 테마 적용 (단순화된 버전 - CSS 클래스만 전환)
export function applyTheme(isDarkMode) {
    const html = document.documentElement;
    const body = document.body;
    
    console.log(`테마 적용: ${isDarkMode ? '다크' : '라이트'} 모드`);
    
    if (isDarkMode) {
        html.classList.add('dark-theme');
        html.classList.remove('light-theme');
        body.classList.add('dark-theme');
        body.classList.remove('light-theme');
    } else {
        html.classList.add('light-theme');
        html.classList.remove('dark-theme');
        body.classList.add('light-theme');
        body.classList.remove('dark-theme');
    }
    
    // 메타 테마 컬러 업데이트 (모바일 브라우저)
    updateMetaThemeColor(isDarkMode);
    
    // 적용된 테마 확인을 위한 로그
    console.log('HTML 클래스:', html.className);
    console.log('CSS 변수 확인 (--primary-bg):', getComputedStyle(html).getPropertyValue('--primary-bg'));
}

// 모바일 브라우저 테마 컬러 업데이트
function updateMetaThemeColor(isDarkMode) {
    let metaThemeColor = document.querySelector('meta[name="theme-color"]');
    
    if (!metaThemeColor) {
        metaThemeColor = document.createElement('meta');
        metaThemeColor.name = 'theme-color';
        document.head.appendChild(metaThemeColor);
    }
    
    metaThemeColor.content = isDarkMode ? '#1a1a1a' : '#ffffff';
}

// 정리
export function cleanup() {
    if (mediaQueryList && mediaQueryList._themeChangeHandler) {
        // 최신 브라우저용
        if (mediaQueryList.removeEventListener) {
            mediaQueryList.removeEventListener('change', mediaQueryList._themeChangeHandler);
        } 
        // 구형 브라우저용
        else if (mediaQueryList.removeListener) {
            mediaQueryList.removeListener(mediaQueryList._themeChangeHandler);
        }
    }
    
    themeDetectionCallback = null;
    mediaQueryList = null;
}