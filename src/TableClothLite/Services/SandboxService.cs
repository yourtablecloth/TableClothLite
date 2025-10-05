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

    public bool ShowWsbDownloadGuide { get; private set; } = false;

    // 대기 중인 다운로드 정보를 저장
    private MemoryStream? _pendingDownloadStream;
    private string? _pendingFileName;
    private ServiceInfo? _pendingServiceInfo;
    private string? _pendingTargetUrl;

    /// <summary>
    /// 대기 중인 서비스 정보를 가져옵니다.
    /// </summary>
    public ServiceInfo? PendingServiceInfo => _pendingServiceInfo;

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

    public async Task GenerateSandboxDocumentAsync(
        string targetUrl,
        ServiceInfo serviceInfo,
        Action? onStateChanged = null,
        CancellationToken cancellationToken = default)
    {
        // OS 감지를 먼저 동기적으로 실행 (사용자 상호작용 컨텍스트 유지)
        bool isWindows = true;
        bool isDesktop = true;
        
        try
        {
            // JavaScript를 통해 OS 감지 - 동기적으로 실행
            var osInfo = await _jsRuntime.InvokeAsync<OsDetectionResult>("detectOS");
            isWindows = osInfo?.IsWindows ?? true;

            // 화면 너비로 데스크톱 여부 감지 (768px 이상을 데스크톱으로 간주)
            var windowWidth = await _jsRuntime.InvokeAsync<int>("getWindowWidth");
            isDesktop = windowWidth >= 768;
            
            IsWindows = isWindows;
            IsDesktop = isDesktop;

            _logger.LogInformation(
                "환경 감지 완료 - OS: {Os}, Desktop: {IsDesktop}, Width: {Width}",
                isWindows ? "Windows" : "Non-Windows",
                isDesktop,
                windowWidth);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "환경 감지 중 오류 발생. 기본값 사용.");
            // 오류 발생 시 안전하게 기본값 사용
            isWindows = true;
            isDesktop = true;
            IsWindows = true;
            IsDesktop = true;
        }

        // Windows 데스크톱 환경이 아닌 경우 가이드 모달 표시
        if (!isWindows || !isDesktop)
        {
            _logger.LogInformation(
                "비호환 환경 감지 - OS: {Os}, Desktop: {IsDesktop}. 가이드 모달 즉시 표시.",
                isWindows ? "Windows" : "Non-Windows",
                isDesktop);

            // 서비스 정보만 저장하고, 가이드 모달을 즉시 표시
            // 파일 생성은 사용자가 "그래도 다운로드" 버튼을 클릭할 때 수행
            _pendingFileName = $"{serviceInfo.ServiceId}.wsb";
            _pendingServiceInfo = serviceInfo;
            _pendingTargetUrl = targetUrl;

            // 가이드 모달을 즉시 표시 (사용자 상호작용 컨텍스트 내)
            ShowWsbDownloadGuide = true;
            onStateChanged?.Invoke();
            return;
        }

        // Windows 데스크톱 환경인 경우 바로 다운로드
        await DownloadWsbFileDirectlyAsync(targetUrl, serviceInfo, cancellationToken);
    }

    /// <summary>
    /// 가이드 모달에서 "그래도 다운로드" 버튼을 눌렀을 때 호출됩니다.
    /// </summary>
    public async Task DownloadPendingFileAsync(CancellationToken cancellationToken = default)
    {
        if (_pendingServiceInfo != null && _pendingFileName != null)
        {
            _logger.LogInformation("사용자가 비호환 환경에서 WSB 파일 다운로드를 선택했습니다.");
            
            try
            {
                // 이제 파일을 생성합니다
                var doc = await _sandboxComposerService.CreateSandboxDocumentAsync(
                    this, _pendingTargetUrl ?? string.Empty, _pendingServiceInfo, cancellationToken).ConfigureAwait(false);
                
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
                _pendingTargetUrl = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WSB 파일 다운로드 중 오류 발생");
                throw;
            }
        }

        ShowWsbDownloadGuide = false;
    }

    /// <summary>
    /// WSB 파일을 직접 다운로드합니다.
    /// </summary>
    private async Task DownloadWsbFileDirectlyAsync(
        string targetUrl,
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken = default)
    {
        var doc = await _sandboxComposerService.CreateSandboxDocumentAsync(
            this, targetUrl, serviceInfo, cancellationToken).ConfigureAwait(false);
        
        using var memStream = new MemoryStream();
        doc.Save(memStream);
        memStream.Position = 0L;

        await _fileDownloadService.DownloadFileAsync(
            memStream, $"{serviceInfo.ServiceId}.wsb", "application/xml",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 가이드 모달을 닫습니다.
    /// </summary>
    public void CloseWsbDownloadGuide()
    {
        ShowWsbDownloadGuide = false;
        
        // 대기 중인 다운로드 정리
        _pendingDownloadStream?.Dispose();
        _pendingDownloadStream = null;
        _pendingFileName = null;
        _pendingServiceInfo = null;
        _pendingTargetUrl = null;
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
