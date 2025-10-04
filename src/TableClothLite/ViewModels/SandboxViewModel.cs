using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TableClothLite.Shared.Models;
using TableClothLite.Services;
using TableClothLite.Shared.Services;
using Microsoft.JSInterop;

namespace TableClothLite.ViewModels;

public sealed partial class SandboxViewModel : ObservableObject
{
    public SandboxViewModel(
        ILogger<SandboxViewModel> logger,
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

    [RelayCommand]
    private Task LoadCatalogAsync()
        => _catalogService.LoadCatalogDocumentAsync(Services);

    // 환경 감지 결과를 저장하는 속성들
    [ObservableProperty]
    private bool _isWindows = true;

    [ObservableProperty]
    private bool _isDesktop = true;

    [ObservableProperty]
    private bool _showWsbDownloadGuide = false;

    // 대기 중인 다운로드 정보를 저장
    private MemoryStream? _pendingDownloadStream;
    private string? _pendingFileName;

    /// <summary>
    /// 환경 감지를 수행합니다.
    /// </summary>
    public async Task DetectEnvironmentAsync()
    {
        try
        {
            // JavaScript를 통해 OS 감지
            var osInfo = await _jsRuntime.InvokeAsync<OsDetectionResult>("detectOS");
            _isWindows = osInfo?.IsWindows ?? true;

            // 화면 너비로 데스크톱 여부 감지 (768px 이상을 데스크톱으로 간주)
            var windowWidth = await _jsRuntime.InvokeAsync<int>("getWindowWidth");
            _isDesktop = windowWidth >= 768;

            _logger.LogInformation(
                "환경 감지 완료 - OS: {Os}, Desktop: {IsDesktop}, Width: {Width}",
                _isWindows ? "Windows" : "Non-Windows",
                _isDesktop,
                windowWidth);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "환경 감지 중 오류 발생. 기본값 사용.");
            // 오류 발생 시 안전하게 기본값 사용
            _isWindows = true;
            _isDesktop = true;
        }
    }

    public async Task GenerateSandboxDocumentAsync(
        string targetUrl,
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken = default)
    {
        // 환경 감지
        await DetectEnvironmentAsync();

        // Windows 데스크톱 환경이 아닌 경우 가이드 모달 표시
        if (!_isWindows || !_isDesktop)
        {
            _logger.LogInformation(
                "비호환 환경 감지 - OS: {Os}, Desktop: {IsDesktop}. 가이드 모달 표시.",
                _isWindows ? "Windows" : "Non-Windows",
                _isDesktop);

            // WSB 파일 생성은 미리 준비
            var doc = await _sandboxComposerService.CreateSandboxDocumentAsync(
                this, targetUrl, serviceInfo, cancellationToken).ConfigureAwait(false);
            
            _pendingDownloadStream = new MemoryStream();
            doc.Save(_pendingDownloadStream);
            _pendingDownloadStream.Position = 0L;
            _pendingFileName = $"{serviceInfo.ServiceId}.wsb";

            // 가이드 모달 표시
            _showWsbDownloadGuide = true;
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
        if (_pendingDownloadStream != null && _pendingFileName != null)
        {
            _logger.LogInformation("사용자가 비호환 환경에서 WSB 파일 다운로드를 선택했습니다.");
            
            await _fileDownloadService.DownloadFileAsync(
                _pendingDownloadStream, 
                _pendingFileName, 
                "application/xml",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // 정리
            _pendingDownloadStream?.Dispose();
            _pendingDownloadStream = null;
            _pendingFileName = null;
        }

        _showWsbDownloadGuide = false;
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
        _showWsbDownloadGuide = false;
        
        // 대기 중인 다운로드 정리
        _pendingDownloadStream?.Dispose();
        _pendingDownloadStream = null;
        _pendingFileName = null;
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

    [ObservableProperty]
    private ObservableCollection<ServiceInfo> _services = new();

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
