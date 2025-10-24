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
using TableClothLite.Components.Chat;

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
    private string? _processingStatus = null; // 멀티턴 처리 상태 메시지
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

    // ModelIndicator 레퍼런스
    private ModelIndicator? _modelIndicator;
    
    // 후원 배너 상태
    private bool _sponsorBannerDismissed = false;

    private List<ExamplePrompt> GetExamplePrompts()
    {
        var list = new List<ExamplePrompt>()
        {
            new("🏦","이번 달 연말정산 자료를 제출하려면 어떤 절차를 따라야 하는지 알려줘.",1),
            new("💰","연말정산 정산분이 급여에 반영됐는지 스스로 확인하는 방법을 알려줘.",2),
            new("💼","법인세를 신고하려면 어떤 서류를 준비하고 어디서 제출해야 하는지 알려줘.",3),
            new("💸","종합소득세를 신고할 때 필요한 서류와 신고 절차를 단계별로 설명해줘.",5),
            new("🏛️","개인지방소득세를 신고하려면 어떤 방법을 이용하면 되는지 알려줘.",5),
            new("🏠","6월 재산세 고지서를 확인하고 납부하려면 어떤 절차를 따르면 되는지 안내해줘.",6),
            new("🏢","주민세 사업소분 신고 대상과 신고 방법을 구체적으로 설명해줘.",7),
            new("🏘️","주민세 개인분을 납부하는 방법과 납부처를 알려줘.",8),
            new("🏡","2기분 재산세 납부 일정과 직접 납부할 수 있는 경로를 안내해줘.",9),
            new("🧾","1기 부가가치세 확정신고를 진행하려면 어떤 절차를 거쳐야 하는지 알려줘.",1),
            new("🧾","2기 부가가치세 확정신고를 준비하려면 어떤 절차와 일정이 필요한지 알려줘.",7),
            new("🧾","1기 부가가치세 예정신고를 하려면 필요한 서류와 절차를 설명해줘.",4),
            new("🧾","2기 부가가치세 예정신고를 진행하기 위한 일정과 준비 과정을 안내해줘.",10),
            new("🚗","자동차세를 연납 신청하려면 어디서, 어떻게 신청할 수 있는지 알려줘.",1),
            new("🚗","2기분 자동차세를 납부하려면 어떤 경로로 진행하면 되는지 설명해줘.",12),
            new("🏦","이번 달 4대보험을 납부하려면 어떤 절차와 기한을 따라야 하는지 알려줘.",0),
            new("💵","원천세를 신고·납부하려면 필요한 절차와 일정이 어떻게 되는지 알려줘.",0),
            new("💳","신용카드 결제일과 납부 금액을 스스로 확인하려면 어떤 방법이 있는지 알려줘.",0),
            new("💳","신용카드 청구서를 확인하려면 어떤 경로를 이용하면 되는지 알려줘.",0),
            new("💳","할부 이자 납입 일정을 관리하기 위한 방법을 안내해줘.",0),
            new("🏠","주택담보대출 이자 납입일을 확인하고 자동이체 상태를 점검하는 방법을 알려줘.",0),
            new("💳","신용대출 상환일과 잔액을 확인하는 방법을 알려줘.",0),
            new("💰","적금이나 청약통장 자동이체 내역을 확인하려면 어떤 절차를 따르면 되는지 알려줘.",0),
            new("🩺","보험료 납입 내역을 스스로 확인하는 방법을 알려줘.",0),
            new("💼","IRP나 연금저축 자동 납입 내역을 조회하고 세액공제 한도를 확인하는 방법을 알려줘.",0),
            new("💼","급여 명세를 확인하는 절차와 급여일 관련 규정을 알려줘.",0),
            new("💡","전기요금 청구 금액과 납부 기한을 확인할 수 있는 방법을 알려줘.",0),
            new("🔥","도시가스요금을 확인하고 납부할 수 있는 방법을 안내해줘.",0),
            new("💧","수도요금 청구 내역과 납부 절차를 알려줘.",0),
            new("📱","통신요금 납부일과 요금을 확인할 수 있는 방법을 안내해줘.",0),
            new("🎬","이번 달 구독 서비스 결제 내역을 확인하고 관리하는 방법을 알려줘.",0),
            new("🔧","렌탈 서비스 요금 납부 내역을 확인하는 방법을 안내해줘.",0),
            new("📈","이번 달 투자 포트폴리오를 점검하는 절차와 참고할 수 있는 지표를 알려줘.",0),
            new("💳","신용점수를 조회하고 개선 방법을 안내해줘.",0),
            new("📓","가계부를 마감하고 다음 달 예산을 계획하는 방법을 알려줘.",0),
            new("🔍","자동이체 내역을 점검하고 불필요한 결제를 확인하는 절차를 설명해줘.",0),
            new("💰","비상자금과 저축률을 점검하는 방법을 알려줘.",0),
            new("💸","연말 세액공제를 위해 IRP 추가 납입 가능 금액을 확인하는 방법을 알려줘.",12),
            new("📜","공시지가나 주택가격에 이의신청하려면 어떤 절차를 거쳐야 하는지 알려줘.",11),
            new("🏠","연말정산 대비 공제 항목을 사전에 점검하는 방법을 알려줘.",11),
            new("💡","전기요금 체납이 발생했을 때 납부 재개 절차를 알려줘.",0),
            new("🚔","교통법규 위반 과태료나 범칙금을 납부하는 방법을 알려줘.",0),
            new("⚖️","법원이나 공공기관에서 부과된 벌금을 납부하는 방법을 알려줘.",0),
            new("🎓","한국장학재단 학자금 대출 상환 일정을 확인하고 납부 방법을 알려줘.",0),
            new("🎓","학자금 대출 상환 유예나 분할상환 신청 방법을 안내해줘.",0),
            new("🏫","대학생 등록금이나 교육비를 납부하는 방법과 시기를 알려줘.",3),
            new("📚","학자금 대출을 신청하려면 어떤 자격과 절차를 거쳐야 하는지 알려줘.",1),
            new("🌐","외국납부세액 환급 제도 폐지에 대응해 해외 배당소득 세금 전략을 어떻게 세울지 알려줘.",0),
            new("📈","배당소득 분리과세 전면 도입 가능성 있는데, 투자자 입장에서 대비할 방법을 알려줘.",0),
            new("🏢","은행의 RWA 규제 완화 정책이 대출 조건에 미칠 영향을 분석해줘.",0),
            new("🆓","신용사면 조치가 시작되면 연체 기록 삭제 절차와 신청 방법을 알려줘.",0),
        };

        var totalItemCount = 3;
        var thisMonthItems = list.Where(x => x.Month == DateTime.Now.Month).Take(2);
        var remainItems = list.Where(x => x.Month == default).OrderBy(x => Guid.NewGuid()).Take(totalItemCount - thisMonthItems.Count());

        return thisMonthItems.Concat(remainItems).ToList();
    }

    // 예시 프롬프트 모델
    private record ExamplePrompt(string Icon, string Text, int Month);

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
        ChatService.ProcessingStatusChanged += OnProcessingStatusChanged;

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

    private void OnProcessingStatusChanged(object? sender, ProcessingStatusEventArgs e)
    {
        _processingStatus = e.Status;
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
            // TODO: Catalog XML 파일과 일치시켜 정확한 DisplayName 찾기
            var defaultService = new ServiceInfo(
                ServiceId: $"web-site-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}",
                DisplayName: "샌드박스에서 웹 사이트 열기", 
                Category: "other",
                Url: url,
                CompatNotes: string.Empty
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
                "샌드박스에서 링크를 열 수 없습니다.", 
                "error");
        }
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
      // ...existing code...
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
      // ...existing code...
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
        ChatService.ProcessingStatusChanged -= OnProcessingStatusChanged;
  
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

    // 대화 내용 인쇄 메서드 - confirm을 좀 더 부드러운 알림로 변경
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
    private Dictionary<int, bool> _copiedStates = new();
    
    private async Task CopyMessageAsync(string content, int messageIndex)
    {
        try
        {
            var success = await JSRuntime.InvokeAsync<bool>("copyToClipboard", content);
            if (success)
            {
                // 복사 성공 - 상태 업데이트
                _copiedStates[messageIndex] = true;
                StateHasChanged();
                
                await SafeInvokeJSAsync("showToast", "메시지가 클립보드에 복사되었습니다.", "success");
                
                // 2초 후 상태 초기화
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000);
                    await InvokeAsync(() =>
                    {
                        _copiedStates[messageIndex] = false;
                        StateHasChanged();
                    });
                });
            }
            else
            {
                await SafeInvokeJSAsync("showToast", "복사하지 않으면 문제가 발생했습니다. 다시 시도해 주세요.", "error");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"메시지 복사 중 오류: {ex.Message}");
            await SafeInvokeJSAsync("showToast", "복사하지 않으면 문제가 발생했습니다.", "error");
        }
    }
    
    private bool IsCopied(int messageIndex)
    {
        return _copiedStates.TryGetValue(messageIndex, out var isCopied) && isCopied;
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
}