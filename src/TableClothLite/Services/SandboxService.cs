using System.Collections.ObjectModel;
using TableClothLite.Shared.Models;
using TableClothLite.Services;
using TableClothLite.Shared.Services;
using Microsoft.JSInterop;

namespace TableClothLite.Services;

public sealed class SandboxService
{
    public SandboxService(
        ILogger<SandboxService> logger,
        FileDownloadService fileDownloadService,
        SandboxComposerService sandboxComposerService,
        CatalogService catalogService,
        IJSRuntime jsRuntime)
    {
        _logger = logger;
        _fileDownloadService = fileDownloadService;
        _sandboxComposerService = sandboxComposerService;
        _catalogService = catalogService;
        _jsRuntime = jsRuntime;
    }

    private readonly ILogger _logger;
    private readonly FileDownloadService _fileDownloadService;
    private readonly SandboxComposerService _sandboxComposerService;
    private readonly CatalogService _catalogService;
    private readonly IJSRuntime _jsRuntime;

    public Task LoadCatalogAsync()
        => _catalogService.LoadCatalogDocumentAsync(Services);

    // 환경 감지 결과를 저장하는 속성들
    public bool IsWindows { get; private set; } = true;

    public bool IsDesktop { get; private set; } = true;

    // 대기 중인 다운로드 정보를 저장
    private MemoryStream? _pendingDownloadStream;
    private string? _pendingFileName;
    private ServiceInfo? _pendingServiceInfo;

    /// <summary>
    /// 대기 중인 서비스 정보를 가져옵니다.
    /// </summary>
    public ServiceInfo? PendingServiceInfo => _pendingServiceInfo;

    /// <summary>
    /// WSB 다운로드 모달 표시 요청 이벤트
    /// </summary>
    public event EventHandler<ServiceInfo>? ShowWsbDownloadGuideRequested;

    /// <summary>
    /// 환경 감지를 수행합니다.
    /// </summary>
    public async Task DetectEnvironmentAsync()
    {
        try
        {
            // JavaScript를 통해 OS 감지
            var osInfo = await _jsRuntime.InvokeAsync<OsDetectionResult>("detectOS");
            IsWindows = osInfo?.IsWindows ?? true;

            // 화면 너비로 데스크톱 여부 감지 (768px 이상을 데스크톱으로 간주)
            var windowWidth = await _jsRuntime.InvokeAsync<int>("getWindowWidth");
            IsDesktop = windowWidth >= 768;

            _logger.LogInformation(
                "환경 감지 완료 - OS: {Os}, Desktop: {IsDesktop}, Width: {Width}",
                IsWindows ? "Windows" : "Non-Windows",
                IsDesktop,
                windowWidth);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "환경 감지 중 오류 발생. 기본값 사용.");
            // 오류 발생 시 안전하게 기본값 사용
            IsWindows = true;
            IsDesktop = true;
        }
    }

    /// <summary>
    /// 특정 서비스(사이트)를 사전 선택한 무설치 .wsb 생성을 준비하고 가이드 모달을 띄웁니다.
    /// </summary>
    public Task GenerateSandboxDocumentAsync(
        ServiceInfo serviceInfo,
        Action? onStateChanged = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("WSB 다운로드 가이드 모달 표시 - 서비스: {ServiceName}", serviceInfo.DisplayName);

        _pendingFileName = string.IsNullOrWhiteSpace(serviceInfo.ServiceId)
            ? "TableCloth-NoInstall.wsb"
            : $"{serviceInfo.ServiceId}.wsb";
        _pendingServiceInfo = serviceInfo;

        // 이벤트 발생으로 모달 표시 요청
        ShowWsbDownloadGuideRequested?.Invoke(this, serviceInfo);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 사이트 사전 선택 없이 일반 런처용 무설치 .wsb 생성을 준비합니다.
    /// </summary>
    public Task GenerateGeneralLauncherAsync(Action? onStateChanged = null)
    {
        var general = new ServiceInfo(
            ServiceId: string.Empty,
            DisplayName: "무설치 식탁보 (일반 런처)",
            Category: "other",
            Url: string.Empty,
            CompatNotes: string.Empty);

        return GenerateSandboxDocumentAsync(general, onStateChanged);
    }

    /// <summary>
    /// 가이드 모달에서 "WSB 다운로드" 버튼을 눌렀을 때 호출됩니다.
    /// </summary>
    public async Task DownloadPendingFileAsync(CancellationToken cancellationToken = default)
    {
        if (_pendingServiceInfo != null && _pendingFileName != null)
        {
            _logger.LogInformation("사용자가 WSB 파일 다운로드를 선택했습니다.");

            try
            {
                // 파일을 생성합니다
                var doc = await _sandboxComposerService.CreateSandboxDocumentAsync(
                    this, _pendingServiceInfo, cancellationToken).ConfigureAwait(false);

                _pendingDownloadStream = new MemoryStream();
                doc.Save(_pendingDownloadStream);
                _pendingDownloadStream.Position = 0L;

                await _fileDownloadService.DownloadFileAsync(
                    _pendingDownloadStream,
                    _pendingFileName,
                    "application/xml",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                // 정리
                _pendingDownloadStream?.Dispose();
                _pendingDownloadStream = null;
                _pendingFileName = null;
                _pendingServiceInfo = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WSB 파일 다운로드 중 오류 발생");
                throw;
            }
        }
    }

    /// <summary>
    /// 가이드 모달을 닫습니다.
    /// </summary>
    public void CloseWsbDownloadGuide()
    {
        // 대기 중인 다운로드 정리
        _pendingDownloadStream?.Dispose();
        _pendingDownloadStream = null;
        _pendingFileName = null;
        _pendingServiceInfo = null;
    }

    public string CalculateAbsoluteUrl(string relativePath)
        => _catalogService.CalculateAbsoluteUrl(relativePath).AbsoluteUri;

    public string DisplayCategoryName(string? category)
        => (category?.ToLowerInvariant()?.Trim()) switch
        {
            "banking" => "은행",
            "financing" => "저축은행",
            "creditcard" => "신용카드",
            "education" => "교육",
            "security" => "증권",
            "insurance" => "보험",
            "government" => "정부",
            "other" => "기타",
            _ => category ?? string.Empty,
        };

    public ObservableCollection<ServiceInfo> Services { get; } = new();

    // OS 감지 결과를 담을 클래스
    private class OsDetectionResult
    {
        public bool IsWindows { get; set; }
        public bool IsMac { get; set; }
        public bool IsLinux { get; set; }
        public bool IsAndroid { get; set; }
        public bool IsIOS { get; set; }
        public string? UserAgent { get; set; }
    }
}
