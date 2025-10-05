using AngleSharp.Html.Parser;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using OpenAI;
using System.Net;
using TableClothLite.Models;
using TableClothLite.Shared.Models;
using TableClothLite.Services;
using TableClothLite.Components;

namespace TableClothLite.Pages;

public partial class Chat : IDisposable
{
    [Inject] private SandboxService SandboxService { get; set; } = default!;
    
    public IEnumerable<IGrouping<string, ServiceInfo>> ServiceGroup =
        Enumerable.Empty<IGrouping<string, ServiceInfo>>();

    private DotNetObjectReference<Chat>? dotNetHelper;
    private string _sessionId = Guid.NewGuid().ToString();
    private List<ChatMessageModel> _messages = [];
    private string _userInput = string.Empty;
    private bool _isStreaming = false;
    private string _currentStreamedMessage = string.Empty;
    private OpenAIClient? _client;
    private MarkdownPipeline? _markdownPipeline;
    private HtmlParser _htmlParser = new HtmlParser();
    
    // API 키 상태 관리
    private bool _hasApiKey = false;
    private bool _isCheckingApiKey = true;

    // 스트리밍 취소를 위한 CancellationTokenSource 추가
    private CancellationTokenSource? _streamingCancellationTokenSource;

    // 글자 수 제한 관련 변수
    private readonly int _maxInputLength = 1000; // 최대 글자 수 제한
    private readonly int _warningThreshold = 100; // 제한에 근접했다고 경고할 잔여 글자 수 기준
    private bool _isNearLimit => _userInput.Length > _maxInputLength - _warningThreshold;

    // Dirty state 관리 - 대화 내용이 있는지 추적
    private bool _hasUnsavedContent => _messages.Any() || !string.IsNullOrWhiteSpace(_userInput);

    // 필요한 서비스들 inject
    [Inject] private OpenRouterAuthService AuthService { get; set; } = default!;

    // Windows Sandbox 가이드 모달 상태 관리
    private bool _showSandboxGuide = false;
    private bool _isWindowsOS = true;
    
    // WSB 다운로드 가이드 모달 상태 관리
    private bool _showWsbDownloadGuide = false;
    private ServiceInfo? _currentService = null;

    // 서비스 목록 모달 상태 관리
    private bool _showServicesModal = false;
    
    // 설정 모달 상태 관리
    private bool _showSettingsModal = false;
    private string _settingsModalInitialTab = "theme";

    // 대화 액션 드롭다운 상태 관리
    private bool _showConversationActionsDropdown = false;
    
    // 메뉴 드롭다운 상태 관리
    private bool _showMenuDropdown = false;

    // 새 버전 알림 상태 관리 - confirm 대신 인앱 알림 사용
    private bool _showUpdateNotification = false;
    private VersionInfo? _pendingUpdate = null;
    private Timer? _updateCheckTimer = null;

    // ModelIndicator 레퍼런스
    private ModelIndicator? _modelIndicator;
    
    // 후원 배너 상태
    private bool _sponsorBannerDismissed = false;

    // 버전 정보 클래스 - 간소화
    private class VersionInfo
    {
        public string? Version { get; set; }
        public string? BuildDate { get; set; }
        public string? Commit { get; set; }
        public string? Branch { get; set; }
    }

    // JavaScript에서 반환할 공유 결과 클래스
    private class ShareResult
    {
        public bool Success { get; set; }
        public string Method { get; set; } = string.Empty;
        public string? Error { get; set; }
    }

    protected override void OnInitialized()
    {
        // 호환성을 위한 /Chat 경로 리다이렉트 처리
        var uri = new Uri(NavigationManager.Uri);
        if (uri.AbsolutePath.Equals("/Chat", StringComparison.OrdinalIgnoreCase))
        {
            NavigationManager.NavigateTo("/", replace: true);
            return;
        }

        // 이벤트 구독
        SandboxService.ShowWsbDownloadGuideRequested += OnShowWsbDownloadGuideRequested;

        _markdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseBootstrap()
            .DisableHtml()
            .Build();

        SandboxService.LoadCatalogAsync()
            .ContinueWith(async (task) => {
                ServiceGroup = SandboxService.Services.GroupBy(x => x.Category.Trim().ToLowerInvariant());
                await InvokeAsync(StateHasChanged);
            });
    }

    private void OnShowWsbDownloadGuideRequested(object? sender, ServiceInfo serviceInfo)
    {
        _currentService = serviceInfo;
        _showWsbDownloadGuide = true;
        InvokeAsync(StateHasChanged);
    }

    protected override async Task OnInitializedAsync()
    {
        await CheckApiKeyStatus();
        await LoadSponsorBannerStatus();
    }
    
    private async Task LoadSponsorBannerStatus()
    {
        try
        {
            var dismissed = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "sponsor-banner-dismissed");
            _sponsorBannerDismissed = !string.IsNullOrEmpty(dismissed) && dismissed == "true";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"후원 배너 상태 로드 중 오류: {ex.Message}");
            _sponsorBannerDismissed = false;
        }
    }

    private async Task CheckApiKeyStatus()
    {
        _isCheckingApiKey = true;
        StateHasChanged();

        try
        {
            // Check if we have an API key stored
            var apiKey = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "openRouterApiKey");
            _hasApiKey = !string.IsNullOrEmpty(apiKey);

            if (_hasApiKey)
            {
                if (_client == null)
                    _client = ChatService.CreateOpenAIClient(apiKey);
            }
            else
            {
                _client = null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"API 키 상태 확인 중 오류: {ex.Message}");
            _hasApiKey = false;
        }
        finally
        {
            _isCheckingApiKey = false;
            StateHasChanged();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            dotNetHelper = DotNetObjectReference.Create(this);
            
            // JavaScript 함수들을 안전하게 초기화
            await InitializeJavaScriptAsync();
            
            // 캐시 무효화 및 버전 체크
            await CheckAppVersionAsync();
        }

        // 스트리밍 중이면 스마트 스크롤, 아니면 일반 스크롤
        if (_isStreaming)
        {
            await SafeInvokeJSAsync("smartScrollToBottom", "messages");
        }
        else
        {
            await SafeInvokeJSAsync("scrollToBottom", "messages");
        }
    }

    // JavaScript 초기화를 안전하게 처리
    private async Task InitializeJavaScriptAsync()
    {
        try
        {
            // 기본 JavaScript 함수들이 로드될 때까지 대기
            var maxAttempts = 50; // 5초 대기 (100ms * 50)
            var attempts = 0;
            
            while (attempts < maxAttempts)
            {
                try
                {
                    // Helpers 객체가 존재하는지 확인
                    var helpersExists = await JSRuntime.InvokeAsync<bool>("eval", "typeof window.Helpers !== 'undefined'");
                    if (helpersExists)
                    {
                        Console.WriteLine("JavaScript Helpers 객체가 준비되었습니다.");
                        break;
                    }
                }
                catch
                {
                    // 계속 시도
                }
                
                attempts++;
                await Task.Delay(100);
            }

            if (attempts >= maxAttempts)
            {
                Console.WriteLine("Warning: JavaScript Helpers 객체를 찾을 수 없습니다. 기본 기능만 사용됩니다.");
                return;
            }

            // Helpers가 준비되면 초기화 진행
            await SafeInvokeJSAsync("Helpers.setDotNetHelper", dotNetHelper);
            await SafeInvokeJSAsync("setupBeforeUnloadHandler", dotNetHelper);
            await SafeInvokeJSAsync("setupDropdownClickOutside", dotNetHelper);
            await SafeInvokeJSAsync("initChatInput");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JavaScript 초기화 중 오류: {ex.Message}");
        }
    }

    // 안전한 JavaScript 호출
    private async Task SafeInvokeJSAsync(string identifier, params object?[] args)
    {
        try
        {
            await JSRuntime.InvokeVoidAsync(identifier, args);
        }
        catch (JSException ex) when (ex.Message.Contains("undefined"))
        {
            Console.WriteLine($"JavaScript 함수 '{identifier}'가 정의되지 않음: {ex.Message}");
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JavaScript 호출 실패 '{identifier}': {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"예상치 못한 오류 '{identifier}': {ex.Message}");
        }
    }

    // JavaScript에서 호출할 수 있는 메서드 - beforeunload 시 unsaved content 확인
    [JSInvokable]
    public bool HasUnsavedContent()
    {
        return _hasUnsavedContent;
    }

    // JavaScript에서 호출할 수 있는 메서드 - 드롭다운 숨기기
    [JSInvokable]
    public Task HideConversationActionsDropdown()
    {
        _showConversationActionsDropdown = false;
        StateHasChanged();
        return Task.CompletedTask;
    }
    
    // JavaScript에서 호출할 수 있는 메서드 - 메뉴 드롭다운 숨기기
    [JSInvokable]
    public Task HideMenuDropdown()
    {
        _showMenuDropdown = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    // JavaScript에서 호출할 수 있는 메서드 - 창 크기 변경 처리
    [JSInvokable]
    public Task OnWindowResize(int width)
    {
        // 창 크기 변경 시 필요한 처리 (예: 모바일/데스크톱 모드 전환)
        // 현재는 로깅만 수행
        Console.WriteLine($"창 크기 변경 감지: {width}px");
        return Task.CompletedTask;
    }

    // JavaScript에서 호출할 수 있는 메서드 - 페이지 포커스 처리
    [JSInvokable]
    public async Task OnPageFocus()
    {
        // 페이지가 포커스를 받았을 때 API 키 상태 재확인
        await CheckApiKeyStatus();
        StateHasChanged();
    }

    // JavaScript에서 호출할 수 있는 메서드 - 샌드박스에서 링크 열기
    [JSInvokable]
    public async Task OpenSandbox(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                Console.WriteLine("URL이 비어있습니다.");
                return;
            }

            Console.WriteLine($"샌드박스에서 URL 열기: {url}");
            
            // SandboxService를 통해 샌드박스에서 URL 열기
            // URL만 있는 경우 기본 서비스 정보 생성
            var defaultService = new ServiceInfo(
                ServiceId: "web-browser",
                DisplayName: "웹 브라우저", 
                Category: "other",
                Url: url,
                CompatNotes: "AI 채팅에서 생성된 링크"
            );
            
            await SandboxService.GenerateSandboxDocumentAsync(url, defaultService, StateHasChanged);
            
            // 모달이 표시되도록 StateHasChanged 호출
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"샌드박스에서 링크 열기 중 오류: {ex.Message}");
            
            // 오류 발생 시 사용자에게 알림
            await SafeInvokeJSAsync("showToast", 
                "샌드박스에서 링크를 열 수 없습니다. Windows Sandbox가 설치되어 있는지 확인해주세요.", 
                "error");
        }
    }

    // JavaScript에서 호출할 수 있는 메서드 - 새 버전 감지 (간소화된 구조)
    [JSInvokable]
    public async Task OnNewVersionDetected(string versionInfoJson)
    {
        try
        {
            if (string.IsNullOrEmpty(versionInfoJson)) return;
            
            using var doc = System.Text.Json.JsonDocument.Parse(versionInfoJson);
            var root = doc.RootElement;
            
            _pendingUpdate = new VersionInfo
            {
                Version = root.TryGetProperty("version", out var version) ? version.GetString() : null,
                BuildDate = root.TryGetProperty("buildDate", out var buildDate) ? buildDate.GetString() : null,
                Commit = root.TryGetProperty("commit", out var commit) ? commit.GetString() : null,
                Branch = root.TryGetProperty("branch", out var branch) ? branch.GetString() : null
            };
            
            // 포커스 빼앗김 없이 부드러운 인앱 알림만 표시
            await ShowGentleUpdateNotificationAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"새 버전 감지 처리 중 오류: {ex.Message}");
        }
    }

    // 앱 버전 체크 및 캐시 관리
    private async Task CheckAppVersionAsync()
    {
        try
        {
            // 현재 앱 버전 (fallback용)
            const string FALLBACK_VERSION = "2024.12.17.1";
            
            // version.json에서 서버 버전 확인
            var serverVersionInfo = await GetServerVersionAsync();
            
            // 로컬 스토리지에서 저장된 정보 확인
            var storedVersion = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-version");
            var dismissedVersions = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-dismissed-versions");
            var dismissedVersionsList = string.IsNullOrEmpty(dismissedVersions) 
                ? new List<string>() 
                : System.Text.Json.JsonSerializer.Deserialize<List<string>>(dismissedVersions) ?? new List<string>();
            
            if (string.IsNullOrEmpty(storedVersion))
            {
                // 처음 방문 - 현재 버전 저장
                var versionToStore = serverVersionInfo?.Version ?? FALLBACK_VERSION;
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "tablecloth-version", versionToStore);
                Console.WriteLine($"첫 방문: 버전 {versionToStore} 저장");
            }
            else if (serverVersionInfo?.Version != null && 
                     storedVersion != serverVersionInfo.Version &&
                     !dismissedVersionsList.Contains(serverVersionInfo.Version))
            {
                // 새 버전 감지하고, 사용자가 이전에 "나중에"를 선택하지 않은 경우만 알림
                _pendingUpdate = serverVersionInfo;
                await ShowGentleUpdateNotificationAsync();
                Console.WriteLine($"새 버전 감지: {serverVersionInfo.Version} (현재: {storedVersion})");
            }
            else if (serverVersionInfo?.Version != null && dismissedVersionsList.Contains(serverVersionInfo.Version))
            {
                Console.WriteLine($"버전 {serverVersionInfo.Version}는 사용자가 이전에 무시함");
            }
            
            // 백그라운드에서 주기적 버전 체크 시작
            StartPeriodicVersionCheck();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"버전 체크 중 오류: {ex.Message}");
        }
    }

    // 서버에서 버전 정보 가져오기 - 간소화된 구조
    private async Task<VersionInfo?> GetServerVersionAsync()
    {
        try
        {
            var cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // JavaScript fetch API 직접 사용하여 더 나은 에러 핸들링
            var fetchResult = await SafeInvokeJSWithResultAsync<string>("fetchVersionJson", $"/version.json?t={cacheBuster}");
            
            if (!string.IsNullOrEmpty(fetchResult))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(fetchResult);
                var root = doc.RootElement;
                
                var versionInfo = new VersionInfo
                {
                    Version = root.TryGetProperty("version", out var version) ? version.GetString() : null,
                    BuildDate = root.TryGetProperty("buildDate", out var buildDate) ? buildDate.GetString() : null,
                    Commit = root.TryGetProperty("commit", out var commit) ? commit.GetString() : null,
                    Branch = root.TryGetProperty("branch", out var branch) ? branch.GetString() : null
                };
                
                Console.WriteLine($"서버 버전 정보 로드 성공: {versionInfo.Version} (빌드: {versionInfo.BuildDate})");
                return versionInfo;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"서버 버전 정보 가져오기 실패: {ex.Message}");
            // 404나 네트워크 오류 시 조용히 처리
        }

        return null;
    }

    // 결과를 반환하는 안전한 JavaScript 호출
    private async Task<T?> SafeInvokeJSWithResultAsync<T>(string identifier, params object[] args)
    {
        try
        {
            return await JSRuntime.InvokeAsync<T>(identifier, args);
        }
        catch (JSException ex) when (ex.Message.Contains("undefined"))
        {
            Console.WriteLine($"JavaScript 함수 '{identifier}'가 정의되지 않음: {ex.Message}");
            return default;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JavaScript 호출 실패 '{identifier}': {ex.Message}");
            return default;
        }
    }

    // 부드러운 업데이트 알림 표시 - confirm 완전 제거
    private Task ShowGentleUpdateNotificationAsync()
    {
        if (_pendingUpdate is null)
            return Task.CompletedTask;

        // 포커스 빼앗김 없는 페이지 통합 알림만 표시
        _showUpdateNotification = true;
        StateHasChanged();

        // 5분 후 자동 숨김 처리 (사용자가 직접 닫지 않은 경우)
        return Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(5));
            await InvokeAsync(() =>
            {
                if (_showUpdateNotification)
                {
                    _showUpdateNotification = false;
                    StateHasChanged();
                }
            });
        });
    }

    // 업데이트 알림 닫기 - 사용자가 "나중에" 선택 시 기억
    private async Task DismissUpdateNotification()
    {
        if (_pendingUpdate is not null)
        {
            // 현재 버전을 "무시된 버전" 목록에 추가
            var dismissedVersions = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-dismissed-versions");
            var dismissedVersionsList = string.IsNullOrEmpty(dismissedVersions) 
                ? new List<string>() 
                : System.Text.Json.JsonSerializer.Deserialize<List<string>>(dismissedVersions) ?? new List<string>();
            
            if (!dismissedVersionsList.Contains(_pendingUpdate.Version ?? ""))
            {
                dismissedVersionsList.Add(_pendingUpdate.Version ?? "");
                
                // 최대 5개 버전만 기억 (너무 많이 쌓이지 않도록)
                if (dismissedVersionsList.Count > 5)
                {
                    dismissedVersionsList.RemoveRange(0, dismissedVersionsList.Count - 5);
                }
                
                var dismissedVersionsJson = System.Text.Json.JsonSerializer.Serialize(dismissedVersionsList);
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "tablecloth-dismissed-versions", dismissedVersionsJson);
                
                Console.WriteLine($"버전 {_pendingUpdate.Version}을 무시 목록에 추가");
            }
        }
        
        _showUpdateNotification = false;
        StateHasChanged();
    }

    // 업데이트 적용 - 무시 목록 정리
    private async Task ApplyUpdateAsync()
    {
        if (_pendingUpdate is null) return;

        // 새 버전을 현재 버전으로 설정
        await JSRuntime.InvokeVoidAsync("localStorage.setItem", "tablecloth-version", _pendingUpdate.Version);
        
        // 무시 목록에서 이전 버전들 제거 (새 버전 적용 시 초기화)
        await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "tablecloth-dismissed-versions");
        
        Console.WriteLine($"버전 {_pendingUpdate.Version}로 업데이트 적용");
        
        // 스마트 새로고침 실행
        await SafeInvokeJSAsync("window.forceRefresh");
    }

    // 주기적 버전 체크 시작 - 빈도 조정
    private void StartPeriodicVersionCheck()
    {
        // 기존 타이머가 있다면 정리
        _updateCheckTimer?.Dispose();

        // 1시간마다 백그라운드에서 체크 (30분에서 증가)
        _updateCheckTimer = new Timer(async _ =>
        {
            try
            {
                await InvokeAsync(async () =>
                {
                    var serverVersionInfo = await GetServerVersionAsync();
                    var storedVersion = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-version");
                    var dismissedVersions = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-dismissed-versions");
                    var dismissedVersionsList = string.IsNullOrEmpty(dismissedVersions) 
                        ? new List<string>() 
                        : System.Text.Json.JsonSerializer.Deserialize<List<string>>(dismissedVersions) ?? new List<string>();
                    
                    if (serverVersionInfo?.Version != null && 
                        storedVersion != serverVersionInfo.Version && 
                        !dismissedVersionsList.Contains(serverVersionInfo.Version) &&
                        !_showUpdateNotification) // 이미 알림이 표시되지 않은 경우만
                    {
                        _pendingUpdate = serverVersionInfo;
                        await ShowGentleUpdateNotificationAsync();
                        Console.WriteLine($"주기적 체크: 새 버전 {serverVersionInfo.Version} 감지");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"주기적 버전 체크 중 오류: {ex.Message}");
            }
        });
        
        // 1시간 후 시작하여 1시간 간격으로 실행
        _updateCheckTimer.Change(TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    private async Task HandleLoginAsync()
    {
        // 직접 인증 플로우 시작
        await AuthService.StartAuthFlowAsync();
    }

    private async Task OpenSettingDialog()
    {
        _showSettingsModal = true;
        StateHasChanged();
        await Task.CompletedTask;
    }
    
    private async Task OpenAIModelSettings()
    {
        _settingsModalInitialTab = "ai";
        _showSettingsModal = true;
        StateHasChanged();
        await Task.CompletedTask;
    }

    private async Task OpenServicesModalAsync()
    {
        _showServicesModal = true;
        StateHasChanged();
        await Task.CompletedTask;
    }

    // 로그아웃 메서드 추가
    private async Task Logout()
    {
        // 진행 중인 스트리밍 작업이 있다면 취소
        if (_isStreaming && _streamingCancellationTokenSource != null)
        {
            _streamingCancellationTokenSource.Cancel();
        }

        // 저장되지 않은 대화 내용이 있다면 부드러운 확인 처리
        if (_hasUnsavedContent)
        {
            // confirm 대신 인앱 확인 다이얼로그 사용 권장
            var shouldLogout = await JSRuntime.InvokeAsync<bool>("confirm", 
                "현재 진행 중인 대화 내용이 있습니다. 로그아웃하면 대화 내용이 사라집니다. 정말 로그아웃하시겠습니까?");
            
            if (!shouldLogout)
            {
                return;
            }
        }

        await JSRuntime.InvokeAsync<string>("localStorage.setItem", "openRouterApiKey", string.Empty);
        
        // 스트리밍 상태 완전 초기화
        _isStreaming = false;
        _currentStreamedMessage = string.Empty;
        _streamingCancellationTokenSource?.Cancel();
        _streamingCancellationTokenSource = null;
        
        // 상태 업데이트
        _hasApiKey = false;
        _client = null;
        _messages.Clear();
        _userInput = string.Empty;
        _sessionId = Guid.NewGuid().ToString();
        
        StateHasChanged();
    }

    // 예시 프롬프트 설정 메서드
    private async Task SetExamplePrompt(string prompt)
    {
        if (!_hasApiKey)
        {
            await HandleLoginAsync();
            return;
        }

        _userInput = prompt;
        StateHasChanged();
        
        // 바로 메시지 전송
        await SendMessage();
    }

    // 입력 내용이 변경될 때 호출되는 메서드
    private async Task OnInputChange(ChangeEventArgs e)
    {
        var newValue = e.Value?.ToString() ?? string.Empty;

        // 최대 길이를 초과하는 경우 잘라내기
        if (newValue.Length > _maxInputLength)
            newValue = newValue.Substring(0, _maxInputLength);

        _userInput = newValue;
        
        // 텍스트 영역 자동 리사이즈
        await SafeInvokeJSAsync("autoResizeTextarea", "chatTextArea");
    }

    private async Task SendMessage()
    {
        if (!_hasApiKey)
        {
            await HandleLoginAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(_userInput) || _isStreaming)
            return;

        var userMessage = new ChatMessageModel { Content = _userInput, IsUser = true };
        _messages.Add(userMessage);

        var input = _userInput;
        _userInput = string.Empty;
        _isStreaming = true;
        _currentStreamedMessage = string.Empty;
        
        // 새로운 스트리밍 작업을 위한 CancellationTokenSource 생성
        _streamingCancellationTokenSource?.Cancel();
        _streamingCancellationTokenSource = new CancellationTokenSource();
        
        StateHasChanged();

        try
        {
            if (_client == null)
                throw new InvalidOperationException("Client is not initialized.");

            // 스트리밍 시작 시 스마트 스크롤 (사용자가 맨 아래에 있으면 스크롤)
            await SafeInvokeJSAsync("smartScrollToBottom", "messages");

            await foreach (var chunk in ChatService.SendMessageStreamingAsync(_client, input, _sessionId, _streamingCancellationTokenSource.Token))
            {
                // 취소가 요청되었는지 확인
                _streamingCancellationTokenSource.Token.ThrowIfCancellationRequested();
                
                _currentStreamedMessage += chunk;
                StateHasChanged();
                await Task.Delay(10); // 자연스러운 타이핑 효과
            }

            _messages.Add(new ChatMessageModel { Content = _currentStreamedMessage, IsUser = false });
        }
        catch (OperationCanceledException)
        {
            // 스트리밍이 취소된 경우 - 오류 메시지 없이 조용히 처리
            Console.WriteLine("스트리밍 작업이 취소되었습니다.");
        }
        catch (Exception ex)
        {
            _messages.Add(new ChatMessageModel
            {
                Content = $"죄송합니다. 오류가 발생했습니다: {ex.Message}",
                IsUser = false
            });
        }
        finally
        {
            _isStreaming = false;
            _currentStreamedMessage = string.Empty;
            StateHasChanged();
        }
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && e.ShiftKey == false)
        {
            await SendMessage();
        }
    }

    private MarkupString FormatMessage(string markdown)
    {
        var html = string.Empty;

        if (string.IsNullOrWhiteSpace(markdown))
            return (MarkupString)html;

        // 마크다운을 HTML로 변환
        html = Markdown.ToHtml(markdown, _markdownPipeline);
        var document = _htmlParser.ParseDocument(html);

        if (document.Body == null)
        {
            Console.Error.WriteLine("Cannot parse fragment element.");
            return (MarkupString)html;
        }

        foreach (var eachAnchorElem in document.QuerySelectorAll("a"))
        {
            var currentHref = (eachAnchorElem.GetAttribute("href") ?? string.Empty).Trim();
            eachAnchorElem.RemoveAttribute("href");
            eachAnchorElem.SetAttribute("onclick", $"window.Helpers.openSandbox('{currentHref}');");
            eachAnchorElem.SetAttribute("style", "font-weight: bold; cursor: pointer; color: #2563eb; text-decoration: underline;");
            eachAnchorElem.InnerHtml = WebUtility.HtmlEncode(currentHref);
        }

        html = document.Body.InnerHtml;

        return (MarkupString)html;
    }

    // WSB 다운로드 가이드 모달 닫기
    private void CloseWsbDownloadGuide()
    {
        _showWsbDownloadGuide = false;
        _currentService = null;
        SandboxService.CloseWsbDownloadGuide();
        StateHasChanged();
    }

    // WSB 파일을 어쨌든 다운로드
    private async Task DownloadWsbAnyway()
    {
        await SandboxService.DownloadPendingFileAsync();
        _showWsbDownloadGuide = false;
        _currentService = null;
        StateHasChanged();
    }

    public void Dispose()
    {
        // 이벤트 구독 해제
        SandboxService.ShowWsbDownloadGuideRequested -= OnShowWsbDownloadGuideRequested;
        
        // 스트리밍 작업 취소 및 정리
        _streamingCancellationTokenSource?.Cancel();
        _streamingCancellationTokenSource?.Dispose();

        // beforeunload 핸들러 정리
        try
        {
            JSRuntime.InvokeVoidAsync("cleanupBeforeUnloadHandler");
        }
        catch
        {
            // Disposal 중 오류는 무시
        }

        // 타이머 정리
        _updateCheckTimer?.Dispose();

        dotNetHelper?.Dispose();
    }

    private async Task ResetConversationAsync()
    {
        // 진행 중인 스트리밍 작업이 있다면 취소
        if (_isStreaming && _streamingCancellationTokenSource != null)
        {
            _streamingCancellationTokenSource.Cancel();
        }

        // 저장되지 않은 대화 내용이 있다면 부드러운 확인 처리
        if (_hasUnsavedContent)
        {
            // confirm 대신 인앱 확인 다이얼로그 사용 권장
            var shouldReset = await JSRuntime.InvokeAsync<bool>("confirm", 
                "현재 진행 중인 대화 내용이 있습니다. 새로운 채팅을 시작하면 현재 대화 내용이 사라집니다. 계속하시겠습니까?");
            
            if (!shouldReset)
            {
                return;
            }
        }

        // 스트리밍 관련 상태 완전 초기화
        _isStreaming = false;
        _currentStreamedMessage = string.Empty;
        _streamingCancellationTokenSource?.Cancel();
        _streamingCancellationTokenSource = null;

        // 대화 내용 초기화
        _messages.Clear();
        _sessionId = Guid.NewGuid().ToString();
        await ChatService.ClearSessionAsync(_sessionId);
        
        StateHasChanged();
    }

    // 대화 액션 드롭다운 토글
    private void ToggleConversationActionsDropdown()
    {
        _showConversationActionsDropdown = !_showConversationActionsDropdown;
        StateHasChanged();
    }
    
    // 메뉴 드롭다운 토글
    private void ToggleMenuDropdown()
    {
        _showMenuDropdown = !_showMenuDropdown;
        StateHasChanged();
    }

    // 드롭다운에서 인쇄 후 숨기기
    private async Task PrintAndHideDropdown()
    {
        await PrintConversationAsync();
        _showMenuDropdown = false;
        StateHasChanged();
    }

    // 드롭다운에서 내보내기 후 숨기기
    private async Task ExportAndHideDropdown()
    {
        await ExportConversationAsTextAsync();
        _showMenuDropdown = false;
        StateHasChanged();
    }

    // 드롭다운에서 공유 후 숨기기
    private async Task ShareAndHideDropdown()
    {
        await ShareConversationAsync();
        _showMenuDropdown = false;
        StateHasChanged();
    }
    
    // 메뉴에서 설정 열고 드롭다운 숨기기
    private async Task OpenSettingDialogAndHideMenu()
    {
        _showSettingsModal = true;
        _showMenuDropdown = false;
        StateHasChanged();
        await Task.CompletedTask;
    }
    
    // 메뉴에서 서비스 모달 열고 드롭다운 숨기기
    private async Task OpenServicesModalAndHideMenu()
    {
        await OpenServicesModalAsync();
        _showMenuDropdown = false;
        StateHasChanged();
    }
    
    // 메뉴에서 로그아웃하고 드롭다운 숨기기
    private async Task LogoutAndHideMenu()
    {
        _showMenuDropdown = false;
        StateHasChanged();
        await Logout();
    }

    // 대화 내용 인쇄 메서드 - confirm을 좀 더 부드러운 알림으로 변경
    private async Task PrintConversationAsync()
    {
        if (!_messages.Any())
        {
            // confirm 대신 단순 alert 사용하거나 토스트 알림으로 변경 가능
            await SafeInvokeJSAsync("showToast", "인쇄할 대화 내용이 없습니다.", "info");
            return;
        }

        // 사용자에게 인쇄 방식 선택 옵션 제공 - 더 부드러운 방식으로 변경 필요시
        // 현재는 기본값으로 미리보기 창 사용
        var printHtml = GeneratePrintHtml();
        await SafeInvokeJSAsync("showPrintPreview", printHtml);
    }

    // 인쇄용 HTML 생성 (간단한 버전)
    private string GeneratePrintHtml()
    {
        var html = new System.Text.StringBuilder();
        html.AppendLine($"<h1>식탁보 AI 대화 기록</h1>");
        html.AppendLine($"<p>생성일: {DateTime.Now:yyyy-MM-dd HH:mm}</p>");
        
        foreach (var message in _messages)
        {
            var sender = message.IsUser ? "사용자" : "AI";
            html.AppendLine($"<div><strong>{sender}:</strong> {message.Content}</div>");
        }
        
        return html.ToString();
    }

    // 대화 내용을 텍스트 파일로 내보내기 - alert을 토스트로 변경 가능
    private async Task ExportConversationAsTextAsync()
    {
        if (!_messages.Any())
        {
            // confirm 대신 부드러운 알림
            await SafeInvokeJSAsync("showToast", "내보낼 대화 내용이 없습니다.", "info");
            return;
        }
        
        var conversationData = new
        {
            exportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            messages = _messages.Select(m => new
            {
                content = m.Content,
                isUser = m.IsUser
            }).ToArray()
        };
        
        var jsonData = System.Text.Json.JsonSerializer.Serialize(conversationData);
        var success = await SafeInvokeJSWithResultAsync<bool>("exportConversationAsText", jsonData);
        
        if (!success)
        {
            await SafeInvokeJSAsync("showToast", "텍스트 파일 내보내기에 실패했습니다.", "error");
        }
    }

    // 대화 내용 공유 - alert를 토스트로 변경
    private async Task ShareConversationAsync()
    {
        if (!_messages.Any())
        {
            await SafeInvokeJSAsync("showToast", "공유할 대화 내용이 없습니다.", "info");
            return;
        }

        try
        {
            var shareText = GenerateShareText();
            
            var shareData = new
            {
                title = "식탁보 AI 대화 기록",
                text = shareText
            };
            
            // JavaScript의 shareContent 함수 호출
            var result = await SafeInvokeJSWithResultAsync<ShareResult>("shareContent", shareData);
            
            if (result?.Success == true)
            {
                switch (result.Method)
                {
                    case "webshare":
                        // Web Share API로 성공적으로 공유됨 - 별도 알림 불필요
                        break;
                    case "clipboard":
                        await SafeInvokeJSAsync("showToast", 
                            "대화 내용이 클립보드에 복사되었습니다. 다른 앱에서 붙여넣기하여 공유할 수 있습니다.", 
                            "success");
                        break;
                }
            }
            else
            {
                // 모든 방법 실패
                await SafeInvokeJSAsync("showToast", 
                    "공유 기능을 사용할 수 없습니다. 대신 텍스트 파일로 내보내기를 사용해보세요.", 
                    "warning");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"대화 공유 중 오류: {ex.Message}");
            await SafeInvokeJSAsync("showToast", "대화 공유에 실패했습니다.", "error");
        }
    }

    // 공유용 텍스트 생성
    private string GenerateShareText()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("식탁보 AI 대화 기록");
        text.AppendLine($"생성일: {DateTime.Now:yyyy년 MM월 dd일 HH:mm}");
        text.AppendLine(new string('=', 40));
        text.AppendLine();
        
        for (int i = 0; i < _messages.Count; i++)
        {
            var message = _messages[i];
            var sender = message.IsUser ? "사용자" : "AI";
            
            text.AppendLine($"[{i + 1}] {sender}:");
            text.AppendLine(message.Content.Trim());
            text.AppendLine();
        }
        
        text.AppendLine(new string('=', 40));
        text.AppendLine("식탁보 AI - https://yourtablecloth.app");
        
        return text.ToString();
    }

    // 모달 닫기 메서드
    private void CloseSandboxGuide()
    {
        _showSandboxGuide = false;
        StateHasChanged();
    }

    // 서비스 목록 모달 닫기
    private void CloseServicesModal()
    {
        _showServicesModal = false;
        StateHasChanged();
    }

    // 설정 모달 닫기 - 모델 인디케이터 새로고침 추가
    private async Task CloseSettingsModal()
    {
        _showSettingsModal = false;
        _settingsModalInitialTab = "theme"; // 기본값으로 리셋
        
        // 모델 설정이 변경되었을 수 있으므로 ModelIndicator 새로고침
        if (_modelIndicator is not null)
        {
            await _modelIndicator.RefreshConfig();
        }
        
        StateHasChanged();
    }

    // 후원 배너 해제
    private async Task DismissSponsorBanner()
    {
        _sponsorBannerDismissed = true;
        StateHasChanged();
        
        // 로컬 스토리지에 저장하여 다음 방문시에도 기억
        try
        {
            await JSRuntime.InvokeVoidAsync("localStorage.setItem", "sponsor-banner-dismissed", "true");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"후원 배너 상태 저장 중 오류: {ex.Message}");
        }
    }

    // 개별 메시지 복사
    private async Task CopyMessageAsync(string content)
    {
        try
        {
            var success = await JSRuntime.InvokeAsync<bool>("copyToClipboard", content);
            if (success)
            {
                await SafeInvokeJSAsync("showToast", "메시지가 클립보드에 복사되었습니다.", "success");
            }
            else
            {
                await SafeInvokeJSAsync("showToast", "복사하지 못했습니다. 다시 시도해주세요.", "error");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"메시지 복사 중 오류: {ex.Message}");
            await SafeInvokeJSAsync("showToast", "복사하지 못했습니다.", "error");
        }
    }
}