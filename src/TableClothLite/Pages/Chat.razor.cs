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

namespace TableClothLite.Pages;

public partial class Chat : IDisposable
{
    public IEnumerable<IGrouping<string, ServiceInfo>> ServiceGroup =
        Enumerable.Empty<IGrouping<string, ServiceInfo>>();

    private DotNetObjectReference<Chat>? dotNetHelper;
    private string _sessionId = Guid.NewGuid().ToString();
    private List<ChatMessage> _messages = [];
    private string _userInput = string.Empty;
    private bool _isStreaming = false;
    private string _currentStreamedMessage = string.Empty;
    private OpenAIClient? _client;
    private MarkdownPipeline? _markdownPipeline;
    private HtmlParser _htmlParser = new HtmlParser();
    
    // API 키 상태 관리
    private bool _hasApiKey = false;
    private bool _isCheckingApiKey = true;

    // 사이드바 상태 관리 (초기값은 false, 화면 크기에 따라 동적 결정)
    private bool _isSidebarOpen = false;
    private bool _isInitialized = false;

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

    // 서비스 목록 모달 상태 관리
    private bool _showServicesModal = false;
    
    // 설정 모달 상태 관리
    private bool _showSettingsModal = false;

    protected override void OnInitialized()
    {
        // 호환성을 위한 /Chat 경로 리다이렉트 처리
        var uri = new Uri(NavigationManager.Uri);
        if (uri.AbsolutePath.Equals("/Chat", StringComparison.OrdinalIgnoreCase))
        {
            NavigationManager.NavigateTo("/", replace: true);
            return;
        }

        _markdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseBootstrap()
            .DisableHtml()
            .Build();

        Model.LoadCatalogCommand.ExecuteAsync(this)
            .ContinueWith(async (task) => {
                ServiceGroup = Model.Services.GroupBy(x => x.Category.Trim().ToLowerInvariant());
                await InvokeAsync(StateHasChanged);
            });
    }

    protected override async Task OnInitializedAsync()
    {
        await CheckApiKeyStatus();
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
            await JSRuntime.InvokeVoidAsync("Helpers.setDotNetHelper", dotNetHelper);
            
            // beforeunload 이벤트 핸들러 등록
            await JSRuntime.InvokeVoidAsync("setupBeforeUnloadHandler", dotNetHelper);
            
            // 캐시 무효화 및 버전 체크
            await CheckAppVersionAsync();
            
            // 초기 화면 크기에 따른 사이드바 상태 설정
            await InitializeSidebarState();
            
            // 입력 필드 자동 리사이즈 스크립트 실행
            await JSRuntime.InvokeVoidAsync("initChatInput");
        }

        await JSRuntime.InvokeVoidAsync("scrollToBottom", "messages");
    }

    // JavaScript에서 호출할 수 있는 메서드 - beforeunload 시 unsaved content 확인
    [JSInvokable]
    public bool HasUnsavedContent()
    {
        return _hasUnsavedContent;
    }

    // 앱 버전 체크 및 캐시 관리
    private async Task CheckAppVersionAsync()
    {
        try
        {
            const string APP_VERSION = "2024.1.0"; // GitHub Actions에서 자동 업데이트
            
            // version.json에서 서버 버전 확인
            var serverVersionInfo = await GetServerVersionAsync();
            
            // 로컬 스토리지에서 저장된 버전 확인
            var storedVersion = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-version");
            
            if (string.IsNullOrEmpty(storedVersion))
            {
                // 처음 방문 - 현재 버전 저장
                var versionToStore = serverVersionInfo?.Version ?? APP_VERSION;
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "tablecloth-version", versionToStore);
            }
            else if (serverVersionInfo != null && storedVersion != serverVersionInfo.Version)
            {
                // 버전이 다름 - 캐시 클리어 필요
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "tablecloth-version", serverVersionInfo.Version);
                
                // 사용자에게 알림 (선택사항)
                var shouldRefresh = await JSRuntime.InvokeAsync<bool>("confirm", 
                    $"새 버전 {serverVersionInfo.Version}이 감지되었습니다. 최신 기능을 사용하려면 새로고침이 필요합니다. 새로고침하시겠습니까?");
                
                if (shouldRefresh)
                {
                    await JSRuntime.InvokeVoidAsync("window.forceRefresh");
                }
            }
            
            // 백그라운드에서 주기적 버전 체크 시작
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMinutes(10)); // 10분 후 시작
                while (true)
                {
                    try
                    {
                        await InvokeAsync(async () =>
                        {
                            await JSRuntime.InvokeVoidAsync("checkForUpdates");
                        });
                        await Task.Delay(TimeSpan.FromMinutes(30)); // 30분마다 체크
                    }
                    catch
                    {
                        await Task.Delay(TimeSpan.FromMinutes(60)); // 오류 시 1시간 후 재시도
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"버전 체크 중 오류: {ex.Message}");
        }
    }

    // 서버에서 버전 정보 가져오기
    private async Task<VersionInfo?> GetServerVersionAsync()
    {
        try
        {
            var cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var response = await JSRuntime.InvokeAsync<string>("fetch", $"/version.json?t={cacheBuster}")
                .AsTask()
                .ContinueWith(async task =>
                {
                    try
                    {
                        return await JSRuntime.InvokeAsync<string>("response.json");
                    }
                    catch
                    {
                        return null;
                    }
                });

            var result = await response;
            if (!string.IsNullOrEmpty(result))
            {
                // 간단한 JSON 파싱 (System.Text.Json 사용)
                using var doc = System.Text.Json.JsonDocument.Parse(result);
                var root = doc.RootElement;
                
                return new VersionInfo
                {
                    Version = root.TryGetProperty("version", out var version) ? version.GetString() : null,
                    Timestamp = root.TryGetProperty("timestamp", out var timestampProp) ? timestampProp.GetString() : null,
                    Commit = root.TryGetProperty("commit", out var commit) ? commit.GetString() : null
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"서버 버전 정보 가져오기 실패: {ex.Message}");
        }

        return null;
    }

    // 버전 정보 클래스
    private class VersionInfo
    {
        public string? Version { get; set; }
        public string? Timestamp { get; set; }
        public string? Commit { get; set; }
    }

    private async Task InitializeSidebarState()
    {
        try
        {
            var windowWidth = await JSRuntime.InvokeAsync<int>("getWindowWidth");
            
            // 데스크톱(768px 초과)에서는 기본적으로 열린 상태
            // 모바일(768px 이하)에서는 기본적으로 닫힌 상태
            _isSidebarOpen = windowWidth > 768;
            _isInitialized = true;
            
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"사이드바 초기 상태 설정 중 오류: {ex.Message}");
            // 오류 발생 시 데스크톱 기본값으로 설정
            _isSidebarOpen = true;
            _isInitialized = true;
            StateHasChanged();
        }
    }

    // JavaScript에서 호출할 수 있는 메서드 (창 크기 변경 시)
    [JSInvokable]
    public Task OnWindowResize(int width)
    {
        var isMobile = width <= 768;
        
        // 모바일에서 데스크톱으로 전환 시 사이드바 열기
        if (!isMobile && !_isSidebarOpen)
        {
            _isSidebarOpen = true;
            StateHasChanged();
        }
        // 데스크톱에서 모바일로 전환 시 사이드바 닫기 (단, 이미 열려있다면)
        else if (isMobile && _isSidebarOpen)
        {
            _isSidebarOpen = false;
            StateHasChanged();
        }

        return Task.CompletedTask;
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

    private async Task OpenServicesModalAsync()
    {
        _showServicesModal = true;
        StateHasChanged();
        await Task.CompletedTask;
    }

    private void ToggleSidebar()
    {
        _isSidebarOpen = !_isSidebarOpen;
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
        await JSRuntime.InvokeVoidAsync("autoResizeTextarea", "chatTextArea");
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

        // 모바일에서 메시지 전송 시 사이드바 닫기
        var windowWidth = await JSRuntime.InvokeAsync<int>("getWindowWidth");
        if (windowWidth <= 768 && _isSidebarOpen)
        {
            _isSidebarOpen = false;
        }

        var userMessage = new ChatMessage { Content = _userInput, IsUser = true };
        _messages.Add(userMessage);

        var input = _userInput;
        _userInput = string.Empty;
        _isStreaming = true;
        _currentStreamedMessage = string.Empty;
        StateHasChanged();

        try
        {
            if (_client == null)
                throw new InvalidOperationException("Client is not initialized.");

            await JSRuntime.InvokeVoidAsync("scrollToBottom", "messages");

            await foreach (var chunk in ChatService.SendMessageStreamingAsync(_client, input, _sessionId))
            {
                _currentStreamedMessage += chunk;
                StateHasChanged();
                await Task.Delay(10); // 자연스러운 타이핑 효과
            }

            _messages.Add(new ChatMessage { Content = _currentStreamedMessage, IsUser = false });
        }
        catch (Exception ex)
        {
            _messages.Add(new ChatMessage
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

    protected async Task Logout()
    {
        // 저장되지 않은 대화 내용이 있다면 확인 다이얼로그 표시
        if (_hasUnsavedContent)
        {
            var shouldLogout = await JSRuntime.InvokeAsync<bool>("confirm", 
                "현재 진행 중인 대화 내용이 있습니다. 로그아웃하면 대화 내용이 사라집니다. 정말 로그아웃하시겠습니까?");
            
            if (!shouldLogout)
            {
                return;
            }
        }

        await JSRuntime.InvokeAsync<string>("localStorage.setItem", "openRouterApiKey", string.Empty);
        
        // 상태 업데이트
        _hasApiKey = false;
        _client = null;
        _messages.Clear();
        _userInput = string.Empty;
        _isStreaming = false;
        _currentStreamedMessage = string.Empty;
        _sessionId = Guid.NewGuid().ToString();
        
        StateHasChanged();
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

    private async Task ResetConversationAsync()
    {
        // 저장되지 않은 대화 내용이 있다면 확인 다이얼로그 표시
        if (_hasUnsavedContent)
        {
            var shouldReset = await JSRuntime.InvokeAsync<bool>("confirm", 
                "현재 진행 중인 대화 내용이 있습니다. 새로운 채팅을 시작하면 현재 대화 내용이 사라집니다. 계속하시겠습니까?");
            
            if (!shouldReset)
            {
                return;
            }
        }

        _messages.Clear();
        _sessionId = Guid.NewGuid().ToString();
        await ChatService.ClearSessionAsync(_sessionId);
        
        // 모바일에서 대화 리셋 시 사이드바 닫기
        var windowWidth = await JSRuntime.InvokeAsync<int>("getWindowWidth");
        if (windowWidth <= 768 && _isSidebarOpen)
        {
            _isSidebarOpen = false;
        }
        
        StateHasChanged();
    }

    // 페이지 포커스 시 API 키 상태 재확인
    [JSInvokable]
    public async Task OnPageFocus()
    {
        await CheckApiKeyStatus();
    }

    [JSInvokable("OpenSandbox")]
    public async Task OpenSandboxAsync(string url)
    {
        // OS 감지
        _isWindowsOS = await DetectWindowsOSAsync();
        
        // 사용자가 "다시 보지 않기" 설정했는지 확인
        var storageKey = _isWindowsOS ? "dont-show-windows-sandbox-guide" : "dont-show-other-os-guide";
        var dontShowGuide = await JSRuntime.InvokeAsync<string>("localStorage.getItem", storageKey);
        
        // 가이드를 보여야 하는 경우 모달 표시
        if (string.IsNullOrEmpty(dontShowGuide))
        {
            _showSandboxGuide = true;
            StateHasChanged();
        }

        // WSB 파일 생성 및 다운로드 (기존 로직)
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri))
            parsedUri = null;

        var hostName = default(string);
        if (parsedUri != null)
            hostName = parsedUri.Host;

        var serviceInfo = default(ServiceInfo);
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            serviceInfo = Model.Services.FirstOrDefault(x =>
            {
                if (!Uri.TryCreate(x.Url, UriKind.Absolute, out var serviceUri))
                    return false;

                // TODO: Public Suffix 기반으로 일치 여부를 판별할 필요가 있음
                var rootHostName = serviceUri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
                if (!hostName.EndsWith(rootHostName, StringComparison.OrdinalIgnoreCase))
                    return false;

                return true;
            });
        }

        var doc = await SandboxComposer.CreateSandboxDocumentAsync(
            Model, parsedUri?.AbsoluteUri, serviceInfo);
        using var memStream = new MemoryStream();
        doc.Save(memStream);
        memStream.Position = 0L;

        await FileDownloader.DownloadFileAsync(
            memStream, $"{serviceInfo?.ServiceId ?? "generated"}.wsb", "application/xml")
            .ConfigureAwait(false);
    }

    // OS 감지 메서드 개선
    private async Task<bool> DetectWindowsOSAsync()
    {
        try
        {
            var osInfo = await JSRuntime.InvokeAsync<OSInfo>("detectOS");
            
            Console.WriteLine($"OS 감지 결과 - IsWindows: {osInfo.IsWindows}, UserAgent: {osInfo.UserAgent}");
            
            return osInfo.IsWindows;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OS 감지 중 오류: {ex.Message}");
            // Fallback: 기존 방식으로 감지
            try
            {
                var userAgent = await JSRuntime.InvokeAsync<string>("eval", "navigator.userAgent");
                var platform = await JSRuntime.InvokeAsync<string>("eval", "navigator.platform");
                
                var isWindows = userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ||
                               platform.Contains("Win", StringComparison.OrdinalIgnoreCase);
                
                return isWindows;
            }
            catch
            {
                // 최종 Fallback: Windows라고 가정
                return true;
            }
        }
    }

    // OS 정보 클래스
    public class OSInfo
    {
        public bool IsWindows { get; set; }
        public bool IsMac { get; set; }
        public bool IsLinux { get; set; }
        public bool IsAndroid { get; set; }
        public bool IsIOS { get; set; }
        public string UserAgent { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
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

    // 설정 모달 닫기
    private void CloseSettingsModal()
    {
        _showSettingsModal = false;
        StateHasChanged();
    }

    public void Dispose()
    {
        // beforeunload 핸들러 정리
        try
        {
            JSRuntime.InvokeVoidAsync("cleanupBeforeUnloadHandler");
        }
        catch
        {
            // Disposal 중 오류는 무시
        }

        dotNetHelper?.Dispose();
    }
}
