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

    // 대화 액션 드롭downs 상태 관리
    private bool _showConversationActionsDropdown = false;

    // 새 버전 알림 상태 관리 - confirm 대신 인앱 알림 사용
    private bool _showUpdateNotification = false;
    private VersionInfo? _pendingUpdate = null;
    private Timer? _updateCheckTimer = null;

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
            
            // 드롭다운 외부 클릭 핸들러 설정
            await JSRuntime.InvokeVoidAsync("setupDropdownClickOutside", dotNetHelper);
            
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

    // JavaScript에서 호출할 수 있는 메서드 - 드롭다운 숨기기
    [JSInvokable]
    public Task HideConversationActionsDropdown()
    {
        _showConversationActionsDropdown = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    // JavaScript에서 호출할 수 있는 메서드 - 새 버전 감지 (confirm 제거)
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
                Timestamp = root.TryGetProperty("timestamp", out var timestamp) ? timestamp.GetString() : null,
                Commit = root.TryGetProperty("commit", out var commit) ? commit.GetString() : null
            };
            
            // 포커스 빼앗김 없이 부드러운 인앱 알림만 표시
            await ShowGentleUpdateNotificationAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"새 버전 감지 처리 중 오류: {ex.Message}");
        }
    }

    // 대화 내용 인쇄 메서드 - confirm을 좀 더 부드러운 알림으로 변경
    private async Task PrintConversationAsync()
    {
        if (!_messages.Any())
        {
            // confirm 대신 단순 alert 사용하거나 토스트 알림으로 변경 가능
            await JSRuntime.InvokeVoidAsync("showToast", "인쇄할 대화 내용이 없습니다.", "info");
            return;
        }

        // 사용자에게 인쇄 방식 선택 옵션 제공 - 더 부드러운 방식으로 변경 필요시
        // 현재는 기본값으로 미리보기 창 사용
        var printHtml = GeneratePrintHtml();
        await JSRuntime.InvokeVoidAsync("showPrintPreview", printHtml);
    }

    // 인쇄용 HTML 생성
    private string GeneratePrintHtml()
    {
        var html = new System.Text.StringBuilder();
        
        // HTML 헤더
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang='ko'>");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset='UTF-8'>");
        html.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        html.AppendLine("<title>TableClothLite AI 대화 기록</title>");
        html.AppendLine("<style>");
        html.AppendLine(GetPrintStyles());
        html.AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        
        // 헤더 정보
        html.AppendLine("<div class='print-header'>");
        html.AppendLine("<h1>TableClothLite AI 대화 기록</h1>");
        html.AppendLine($"<p class='print-date'>생성일: {DateTime.Now:yyyy년 MM월 dd일 HH:mm}</p>");
        html.AppendLine($"<p class='print-info'>총 {_messages.Count}개의 메시지</p>");
        html.AppendLine("</div>");
        
        // 대화 내용
        html.AppendLine("<div class='conversation'>");
        
        for (int i = 0; i < _messages.Count; i++)
        {
            var message = _messages[i];
            var messageClass = message.IsUser ? "user-message" : "assistant-message";
            var sender = message.IsUser ? "사용자" : "TableClothLite AI";
            
            html.AppendLine($"<div class='message {messageClass}'>");
            html.AppendLine($"<div class='message-header'>");
            html.AppendLine($"<span class='sender'>{sender}</span>");
            html.AppendLine($"<span class='message-number'>#{i + 1}</span>");
            html.AppendLine("</div>");
            html.AppendLine($"<div class='message-content'>");
            
            // 마크다운을 HTML로 변환하되 인쇄용으로 정리
            var content = ConvertMarkdownForPrint(message.Content);
            html.AppendLine(content);
            
            html.AppendLine("</div>");
            html.AppendLine("</div>");
        }
        
        html.AppendLine("</div>");
        
        // 푸터
        html.AppendLine("<div class='print-footer'>");
        html.AppendLine("<p>TableClothLite AI - 금융과 공공 부문 AI 어시스턴트</p>");
        html.AppendLine("<p>https://yourtablecloth.app</p>");
        html.AppendLine("</div>");
        
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        
        return html.ToString();
    }

    // 인쇄용 CSS 스타일
    private string GetPrintStyles()
    {
        return @"
            @media screen {
                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                    line-height: 1.6;
                    color: #333;
                    max-width: 800px;
                    margin: 0 auto;
                    padding: 20px;
                    background: #f9f9f9;
                }
            }
            
            @media print {
                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                    line-height: 1.5;
                    color: #000;
                    margin: 0;
                    padding: 15mm;
                    background: white;
                }
                
                .print-header {
                    border-bottom: 2px solid #333;
                    padding-bottom: 10px;
                    margin-bottom: 20px;
                }
                
                .message {
                    page-break-inside: avoid;
                    break-inside: avoid;
                }
                
                .print-footer {
                    position: fixed;
                    bottom: 10mm;
                    left: 15mm;
                    right: 15mm;
                    border-top: 1px solid #ccc;
                    padding-top: 5px;
                    font-size: 0.8em;
                    text-align: center;
                    color: #666;
                }
            }
            
            .print-header h1 {
                margin: 0 0 10px 0;
                font-size: 24px;
                color: #2563eb;
            }
            
            .print-date, .print-info {
                margin: 5px 0;
                color: #666;
                font-size: 14px;
            }
            
            .conversation {
                margin: 20px 0;
            }
            
            .message {
                margin-bottom: 20px;
                border: 1px solid #e5e7eb;
                border-radius: 8px;
                overflow: hidden;
            }
            
            .message-header {
                background: #f3f4f6;
                padding: 8px 12px;
                display: flex;
                justify-content: space-between;
                align-items: center;
                font-size: 12px;
                color: #6b7280;
            }
            
            .user-message .message-header {
                background: #eff6ff;
            }
            
            .assistant-message .message-header {
                background: #f9fafb;
            }
            
            .sender {
                font-weight: 600;
                color: #374151;
            }
            
            .user-message .sender {
                color: #2563eb;
            }
            
            .assistant-message .sender {
                color: #059669;
            }
            
            .message-number {
                font-size: 11px;
                color: #9ca3af;
            }
            
            .message-content {
                padding: 12px;
                background: white;
            }
            
            .message-content h1, .message-content h2, .message-content h3,
            .message-content h4, .message-content h5, .message-content h6 {
                margin-top: 0;
                margin-bottom: 10px;
                color: #374151;
            }
            
            .message-content p {
                margin: 0 0 10px 0;
            }
            
            .message-content ul, .message-content ol {
                margin: 0 0 10px 20px;
                padding-left: 0;
            }
            
            .message-content li {
                margin-bottom: 5px;
            }
            
            .message-content pre {
                background: #f3f4f6;
                padding: 10px;
                border-radius: 4px;
                overflow-x: auto;
                font-size: 12px;
                margin: 10px 0;
            }
            
            .message-content code {
                background: #f3f4f6;
                padding: 2px 4px;
                border-radius: 3px;
                font-size: 13px;
            }
            
            .message-content blockquote {
                border-left: 3px solid #d1d5db;
                margin: 10px 0;
                padding: 5px 0 5px 15px;
                color: #6b7280;
                font-style: italic;
            }
            
            .message-content table {
                border-collapse: collapse;
                width: 100%;
                margin: 10px 0;
                font-size: 13px;
            }
            
            .message-content th, .message-content td {
                border: 1px solid #d1d5db;
                padding: 6px 8px;
                text-align: left;
            }
            
            .message-content th {
                background: #f9fafb;
                font-weight: 600;
            }
            
            .print-footer p {
                margin: 2px 0;
            }
            
            @page {
                margin: 15mm;
                size: A4;
            }
        ";
    }

    // 마크다운을 인쇄용 HTML로 변환
    private string ConvertMarkdownForPrint(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        try
        {
            // 마크다운을 HTML로 변환
            var html = Markdown.ToHtml(markdown, _markdownPipeline);
            var document = _htmlParser.ParseDocument(html);

            if (document.Body == null)
                return WebUtility.HtmlEncode(markdown);

            // 인쇄용으로 링크 처리 (href 제거하고 텍스트로 표시)
            foreach (var anchor in document.QuerySelectorAll("a"))
            {
                var href = anchor.GetAttribute("href") ?? string.Empty;
                if (!string.IsNullOrEmpty(href))
                {
                    anchor.RemoveAttribute("href");
                    anchor.RemoveAttribute("onclick");
                    anchor.SetAttribute("style", "color: #2563eb; text-decoration: underline;");
                    
                    // 링크 URL을 텍스트 뒤에 괄호로 추가
                    if (anchor.TextContent != href)
                    {
                        anchor.InnerHtml = $"{anchor.InnerHtml} ({WebUtility.HtmlEncode(href)})";
                    }
                }
            }

            // 이미지 처리 (alt 텍스트로 대체)
            foreach (var img in document.QuerySelectorAll("img"))
            {
                var alt = img.GetAttribute("alt") ?? "이미지";
                var src = img.GetAttribute("src") ?? "";
                
                var replacement = document.CreateElement("span");
                replacement.SetAttribute("style", "background: #f3f4f6; padding: 4px 8px; border-radius: 4px; font-style: italic;");
                replacement.TextContent = $"[이미지: {alt}]";
                
                img.ParentElement?.ReplaceChild(replacement, img);
            }

            return document.Body.InnerHtml;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"마크다운 변환 중 오류: {ex.Message}");
            return WebUtility.HtmlEncode(markdown);
        }
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
                // 새 버전 감지 - gentle reminder 표시
                _pendingUpdate = serverVersionInfo;
                await ShowGentleUpdateNotificationAsync();
            }
            
            // 백그라운드에서 주기적 버전 체크 시작
            StartPeriodicVersionCheck();
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

    // 부드러운 업데이트 알림 표시 - confirm 완전 제거
    private async Task ShowGentleUpdateNotificationAsync()
    {
        if (_pendingUpdate is null) return;

        // 포커스 빼앗김 없는 페이지 통합 알림만 표시
        _showUpdateNotification = true;
        StateHasChanged();

        // 60초 후 자동 숨김 처리 (사용자가 직접 닫지 않은 경우)
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(60));
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

    // 업데이트 알림 닫기
    private void DismissUpdateNotification()
    {
        _showUpdateNotification = false;
        StateHasChanged();
    }

    // 업데이트 적용
    private async Task ApplyUpdateAsync()
    {
        if (_pendingUpdate is null) return;

        // 버전 정보 업데이트
        await JSRuntime.InvokeVoidAsync("localStorage.setItem", "tablecloth-version", _pendingUpdate.Version);
        
        // 스마트 새로고침 실행
        await JSRuntime.InvokeVoidAsync("window.forceRefresh");
    }

    // 주기적 버전 체크 시작
    private void StartPeriodicVersionCheck()
    {
        // 기존 타이머가 있다면 정리
        _updateCheckTimer?.Dispose();

        // 30분마다 백그라운드에서 체크
        _updateCheckTimer = new Timer(async _ =>
        {
            try
            {
                await InvokeAsync(async () =>
                {
                    var serverVersionInfo = await GetServerVersionAsync();
                    var storedVersion = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-version");
                    
                    if (serverVersionInfo != null && 
                        storedVersion != serverVersionInfo.Version && 
                        !_showUpdateNotification) // 이미 알림이 표시되지 않은 경우만
                    {
                        _pendingUpdate = serverVersionInfo;
                        await ShowGentleUpdateNotificationAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"주기적 버전 체크 중 오류: {ex.Message}");
            }
        });
        
        // 30분 후 시작하여 30분 간격으로 실행
        _updateCheckTimer.Change(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
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

    // 대화 액션 드롭다운 토글
    private void ToggleConversationActionsDropdown()
    {
        _showConversationActionsDropdown = !_showConversationActionsDropdown;
        StateHasChanged();
    }

    // 드롭downs에서 인쇄 후 숨기기
    private async Task PrintAndHideDropdown()
    {
        await PrintConversationAsync();
        _showConversationActionsDropdown = false;
        StateHasChanged();
    }

    // 드롭downs에서 내보내기 후 숨기기
    private async Task ExportAndHideDropdown()
    {
        await ExportConversationAsTextAsync();
        _showConversationActionsDropdown = false;
        StateHasChanged();
    }

    // 드롭down에서 공유 후 숨기기
    private async Task ShareAndHideDropdown()
    {
        await ShareConversationAsync();
        _showConversationActionsDropdown = false;
        StateHasChanged();
    }

    // 대화 내용을 텍스트 파일로 내보내기 - alert를 토스트로 변경 가능
    private async Task ExportConversationAsTextAsync()
    {
        if (!_messages.Any())
        {
            // confirm 대신 부드러운 알림
            await JSRuntime.InvokeVoidAsync("showToast", "내보낼 대화 내용이 없습니다.", "info");
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
        var success = await JSRuntime.InvokeAsync<bool>("exportConversationAsText", jsonData);
        
        if (!success)
        {
            await JSRuntime.InvokeVoidAsync("showToast", "텍스트 파일 내보내기에 실패했습니다.", "error");
        }
    }

    private async Task ResetConversationAsync()
    {
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

    // 대화 내용 공유 - alert를 토스트로 변경
    private async Task ShareConversationAsync()
    {
        if (!_messages.Any())
        {
            await JSRuntime.InvokeVoidAsync("showToast", "공유할 대화 내용이 없습니다.", "info");
            return;
        }

        try
        {
            var shareText = GenerateShareText();
            
            var shareData = new
            {
                title = "TableClothLite AI 대화 기록",
                text = shareText
            };
            
            // JavaScript의 shareContent 함수 호출
            var result = await JSRuntime.InvokeAsync<ShareResult>("shareContent", shareData);
            
            if (result.Success)
            {
                switch (result.Method)
                {
                    case "webshare":
                        // Web Share API로 성공적으로 공유됨 - 별도 알림 불필요
                        break;
                    case "clipboard":
                        await JSRuntime.InvokeVoidAsync("showToast", 
                            "대화 내용이 클립보드에 복사되었습니다. 다른 앱에서 붙여넣기하여 공유할 수 있습니다.", 
                            "success");
                        break;
                }
            }
            else
            {
                // 모든 방법 실패
                await JSRuntime.InvokeVoidAsync("showToast", 
                    "공유 기능을 사용할 수 없습니다. 대신 텍스트 파일로 내보내기를 사용해보세요.", 
                    "warning");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"대화 공유 중 오류: {ex.Message}");
            await JSRuntime.InvokeVoidAsync("showToast", "대화 공유에 실패했습니다.", "error");
        }
    }
    
    // 버전 정보 클래스
    private class VersionInfo
    {
        public string? Version { get; set; }
        public string? Timestamp { get; set; }
        public string? Commit { get; set; }
    }

    // JavaScript에서 반환할 공유 결과 클래스
    private class ShareResult
    {
        public bool Success { get; set; }
        public string Method { get; set; } = string.Empty;
        public string? Error { get; set; }
    }

    // 공유용 텍스트 생성
    private string GenerateShareText()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("TableClothLite AI 대화 기록");
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
        text.AppendLine("TableClothLite AI - https://yourtablecloth.app");
        
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

        // 타이머 정리
        _updateCheckTimer?.Dispose();

        dotNetHelper?.Dispose();
    }
}
