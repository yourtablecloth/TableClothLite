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

    // 필요한 서비스들 inject
    [Inject] private OpenRouterAuthService AuthService { get; set; } = default!;

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
            
            // 초기 화면 크기에 따른 사이드바 상태 설정
            await InitializeSidebarState();
            
            // 입력 필드 자동 리사이즈 스크립트 실행
            await JSRuntime.InvokeVoidAsync("initChatInput");
        }

        await JSRuntime.InvokeVoidAsync("scrollToBottom", "messages");
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
        // 설정 기능은 준비 중
        await JSRuntime.InvokeVoidAsync("alert", "설정 기능은 준비 중입니다.");
    }

    private async Task OpenServicesModalAsync()
    {
        // 서비스 목록 기능은 준비 중
        await JSRuntime.InvokeVoidAsync("alert", "서비스 목록 기능은 준비 중입니다.");
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

    public void Dispose()
    {
        dotNetHelper?.Dispose();
    }
}
