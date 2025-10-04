using Microsoft.JSInterop;
using System.Text.Json;

namespace TableClothLite.Services;

public class ThemeService : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private DotNetObjectReference<ThemeService>? _dotNetObjectRef;

    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
    
    public ThemeMode CurrentTheme { get; private set; } = ThemeMode.Auto;
    public bool IsDarkMode { get; private set; } = false;

    public ThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
        _dotNetObjectRef = DotNetObjectReference.Create(this);
    }

    public async Task InitializeAsync()
    {
        try
        {
            // 저장된 테마 설정 로드
            var savedTheme = await GetSavedThemeAsync();
            CurrentTheme = savedTheme;
            
            // 다크 모드 상태 결정
            IsDarkMode = await DetermineDarkModeAsync(CurrentTheme);
            
            // 테마 적용
            await SetThemeAsync(CurrentTheme);
            
            // 시스템 테마 변경 감지 설정 (JavaScript 모듈 없이)
            await _jsRuntime.InvokeVoidAsync("eval", $@"
                (function() {{
                    if (window.matchMedia) {{
                        const darkModeMediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
                        
                        const handleChange = async (e) => {{
                            console.log('🔄 시스템 테마 변경 감지:', e.matches ? '다크 모드' : '라이트 모드');
                            const currentTheme = localStorage.getItem('theme-preference');
                            if (currentTheme === 'Auto') {{
                                console.log('⚡ 자동 모드이므로 테마 자동 변경');
                                // 직접 테마 적용
                                const html = document.documentElement;
                                const body = document.body;
                                
                                if (e.matches) {{
                                    html.classList.add('dark-theme');
                                    html.classList.remove('light-theme');
                                    body.classList.add('dark-theme');
                                    body.classList.remove('light-theme');
                                }} else {{
                                    html.classList.add('light-theme');
                                    html.classList.remove('dark-theme');
                                    body.classList.add('light-theme');
                                    body.classList.remove('dark-theme');
                                }}
                            }}
                        }};
                        
                        darkModeMediaQuery.addEventListener('change', handleChange);
                        
                        // 정리 함수를 전역으로 저장
                        window.cleanupThemeListener = () => {{
                            darkModeMediaQuery.removeEventListener('change', handleChange);
                        }};
                    }}
                }})();
            ");
            
            // 초기 테마 변경 이벤트 발생
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(CurrentTheme, IsDarkMode));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"테마 서비스 초기화 중 오류: {ex.Message}");
        }
    }

    public async Task SetThemeAsync(ThemeMode theme)
    {
        var previousTheme = CurrentTheme;
        CurrentTheme = theme;
        
        // localStorage에 저장
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "theme-preference", theme.ToString());
        
        // 실제 다크 모드 적용 여부 결정
        var shouldApplyDark = await DetermineDarkModeAsync(theme);
        IsDarkMode = shouldApplyDark;
        
        // JavaScript를 통해 직접 테마 적용 (모듈 import 없이)
        try 
        {
            await _jsRuntime.InvokeVoidAsync("eval", $@"
                (function() {{
                    const html = document.documentElement;
                    const body = document.body;
                    
                    console.log('🎨 테마 변경 시작:', '{theme}', '다크 모드:', {shouldApplyDark.ToString().ToLower()});
                    
                    if ({shouldApplyDark.ToString().ToLower()}) {{
                        html.classList.add('dark-theme');
                        html.classList.remove('light-theme');
                        body.classList.add('dark-theme');
                        body.classList.remove('light-theme');
                        console.log('✅ 다크 테마 클래스 적용됨');
                    }} else {{
                        html.classList.add('light-theme');
                        html.classList.remove('dark-theme');
                        body.classList.add('light-theme');
                        body.classList.remove('dark-theme');
                        console.log('✅ 라이트 테마 클래스 적용됨');
                    }}
                    
                    // 메타 테마 컬러 업데이트
                    let metaThemeColor = document.querySelector('meta[name=""theme-color""]');
                    if (!metaThemeColor) {{
                        metaThemeColor = document.createElement('meta');
                        metaThemeColor.name = 'theme-color';
                        document.head.appendChild(metaThemeColor);
                    }}
                    metaThemeColor.content = {shouldApplyDark.ToString().ToLower()} ? '#1a1a1a' : '#ffffff';
                    
                    console.log('📋 최종 HTML 클래스:', html.className);
                    
                    // CSS 변수 확인
                    const computedStyle = getComputedStyle(html);
                    console.log('🎨 적용된 CSS 변수:', {{
                        '--primary-bg': computedStyle.getPropertyValue('--primary-bg').trim(),
                        '--primary-text': computedStyle.getPropertyValue('--primary-text').trim()
                    }});
                }})();
            ");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"테마 적용 중 오류: {ex.Message}");
        }

        // 디버깅 로그
        Console.WriteLine($"테마 변경: {previousTheme} -> {CurrentTheme}, 다크 모드 적용: {shouldApplyDark}");
        
        // 이벤트 발생
        ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(CurrentTheme, IsDarkMode));
    }

    private async Task<bool> DetermineDarkModeAsync(ThemeMode theme)
    {
        switch (theme)
        {
            case ThemeMode.Dark:
                return true;
            case ThemeMode.Light:
                return false;
            case ThemeMode.Auto:
                try
                {
                    // 시스템 다크 모드 상태 확인
                    return await _jsRuntime.InvokeAsync<bool>("eval", @"
                        window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches
                    ");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"시스템 다크 모드 확인 중 오류: {ex.Message}");
                    return false; // 기본값
                }
            default:
                return false;
        }
    }

    [JSInvokable]
    public Task OnSystemThemeChanged(bool isDarkMode)
    {
        if (CurrentTheme == ThemeMode.Auto)
        {
            var previousDarkMode = IsDarkMode;
            IsDarkMode = isDarkMode;
            
            if (previousDarkMode != IsDarkMode)
            {
                ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(CurrentTheme, IsDarkMode));
            }
        }

        return Task.CompletedTask;
    }

    private async Task<ThemeMode> GetSavedThemeAsync()
    {
        try
        {
            var themeString = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "theme-preference");
            if (Enum.TryParse<ThemeMode>(themeString, out var theme))
            {
                return theme;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"저장된 테마 로드 중 오류: {ex.Message}");
        }
        
        return ThemeMode.Auto; // 기본값
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // JavaScript 모듈 대신 전역 정리 함수 호출
            await _jsRuntime.InvokeVoidAsync("eval", @"
                if (window.cleanupThemeListener) {
                    window.cleanupThemeListener();
                    delete window.cleanupThemeListener;
                }
            ");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"테마 서비스 정리 중 오류: {ex.Message}");
        }
        
        _dotNetObjectRef?.Dispose();
    }
}

public enum ThemeMode
{
    Auto,
    Light,
    Dark
}

public class ThemeChangedEventArgs : EventArgs
{
    public ThemeMode Theme { get; }
    public bool IsDarkMode { get; }

    public ThemeChangedEventArgs(ThemeMode theme, bool isDarkMode)
    {
        Theme = theme;
        IsDarkMode = isDarkMode;
    }
}