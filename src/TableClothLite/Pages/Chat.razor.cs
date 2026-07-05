using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TableClothLite.Services;
using TableClothLite.Shared.Models;

namespace TableClothLite.Pages;

public partial class Chat : IDisposable
{
    [Inject] private SandboxService SandboxService { get; set; } = default!;
    [Inject] private WebMcpInteropService WebMcp { get; set; } = default!;

    private DotNetObjectReference<Chat>? dotNetHelper;

    // 환경 감지
    private bool _isWindowsOS = true;

    // 모달 상태
    private bool _showSandboxGuide = false;
    private bool _showWsbDownloadGuide = false;
    private ServiceInfo? _currentService = null;
    private bool _showServicesModal = false;
    private bool _showSettingsModal = false;
    private string _settingsModalInitialTab = "theme";

    // 메뉴 드롭다운
    private bool _showMenuDropdown = false;

    // 후원 배너
    private bool _sponsorBannerDismissed = false;

    // 새 버전 알림
    private bool _showUpdateNotification = false;
    private VersionInfo? _pendingUpdate = null;
    private Timer? _updateCheckTimer = null;

    private class VersionInfo
    {
        public string? Version { get; set; }
        public string? BuildDate { get; set; }
        public string? Commit { get; set; }
        public string? Branch { get; set; }
    }

    protected override void OnInitialized()
    {
        // WSB 다운로드 가이드 모달 표시 요청 구독
        SandboxService.ShowWsbDownloadGuideRequested += OnShowWsbDownloadGuideRequested;

        // 카탈로그 미리 로드 (서비스 목록 모달에서 사용)
        SandboxService.LoadCatalogAsync()
            .ContinueWith(async (task) =>
            {
                await InvokeAsync(StateHasChanged);
            });
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadSponsorBannerStatus();

        try
        {
            await SandboxService.DetectEnvironmentAsync();
            _isWindowsOS = SandboxService.IsWindows;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"환경 감지 중 오류: {ex.Message}");
            _isWindowsOS = true;
        }

        StateHasChanged();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            dotNetHelper = DotNetObjectReference.Create(this);

            // 버전 알림용 DotNet 참조 등록 (app.js 의 checkForUpdates 가 OnNewVersionDetected 를 호출)
            await SafeInvokeJSAsync("Helpers.setDotNetHelper", dotNetHelper);

            await CheckAppVersionAsync();

            // WebMCP 도구 등록 (지원 브라우저에서만, 미지원 시 조용히 폴백)
            await WebMcp.InitializeAsync();
        }
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

    // ===== WSB 다운로드 흐름 =====

    private void OnShowWsbDownloadGuideRequested(object? sender, ServiceInfo serviceInfo)
    {
        _currentService = serviceInfo;
        _showWsbDownloadGuide = true;
        InvokeAsync(StateHasChanged);
    }

    // 사이트 사전 선택 없는 일반 런처 .wsb 생성
    private async Task DownloadGeneralLauncher()
    {
        await SandboxService.GenerateGeneralLauncherAsync(StateHasChanged);
    }

    // 가이드 모달에서 실제 다운로드 실행
    private async Task DownloadWsbAnyway()
    {
        await SandboxService.DownloadPendingFileAsync();
        _showWsbDownloadGuide = false;
        _currentService = null;
        StateHasChanged();
    }

    private void CloseWsbDownloadGuide()
    {
        _showWsbDownloadGuide = false;
        _currentService = null;
        SandboxService.CloseWsbDownloadGuide();
        StateHasChanged();
    }

    // ===== 메뉴/모달 =====

    private void ToggleMenuDropdown()
    {
        _showMenuDropdown = !_showMenuDropdown;
        StateHasChanged();
    }

    [JSInvokable]
    public Task HideMenuDropdown()
    {
        _showMenuDropdown = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private void OpenServicesModal()
    {
        _showServicesModal = true;
        StateHasChanged();
    }

    private void OpenServicesModalAndHideMenu()
    {
        _showServicesModal = true;
        _showMenuDropdown = false;
        StateHasChanged();
    }

    private void CloseServicesModal()
    {
        _showServicesModal = false;
        StateHasChanged();
    }

    private void OpenSettingDialogAndHideMenu()
    {
        _settingsModalInitialTab = "theme";
        _showSettingsModal = true;
        _showMenuDropdown = false;
        StateHasChanged();
    }

    private void CloseSettingsModal()
    {
        _showSettingsModal = false;
        _settingsModalInitialTab = "theme";
        StateHasChanged();
    }

    private void OpenSandboxGuideAndHideMenu()
    {
        _showSandboxGuide = true;
        _showMenuDropdown = false;
        StateHasChanged();
    }

    private void CloseSandboxGuide()
    {
        _showSandboxGuide = false;
        StateHasChanged();
    }

    private async Task DismissSponsorBanner()
    {
        _sponsorBannerDismissed = true;
        StateHasChanged();

        try
        {
            await JSRuntime.InvokeVoidAsync("localStorage.setItem", "sponsor-banner-dismissed", "true");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"후원 배너 상태 저장 중 오류: {ex.Message}");
        }
    }

    // ===== 새 버전 감지/알림 =====

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

            await ShowGentleUpdateNotificationAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"새 버전 감지 처리 중 오류: {ex.Message}");
        }
    }

    private async Task CheckAppVersionAsync()
    {
        try
        {
            const string FALLBACK_VERSION = "2024.12.17.1";

            var serverVersionInfo = await GetServerVersionAsync();

            var storedVersion = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-version");
            var dismissedVersions = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-dismissed-versions");
            var dismissedVersionsList = string.IsNullOrEmpty(dismissedVersions)
                ? new List<string>()
                : System.Text.Json.JsonSerializer.Deserialize<List<string>>(dismissedVersions) ?? new List<string>();

            if (string.IsNullOrEmpty(storedVersion))
            {
                var versionToStore = serverVersionInfo?.Version ?? FALLBACK_VERSION;
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "tablecloth-version", versionToStore);
            }
            else if (serverVersionInfo?.Version != null &&
                     storedVersion != serverVersionInfo.Version &&
                     !dismissedVersionsList.Contains(serverVersionInfo.Version))
            {
                _pendingUpdate = serverVersionInfo;
                await ShowGentleUpdateNotificationAsync();
            }

            StartPeriodicVersionCheck();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"버전 체크 중 오류: {ex.Message}");
        }
    }

    private async Task<VersionInfo?> GetServerVersionAsync()
    {
        try
        {
            var cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var fetchResult = await SafeInvokeJSWithResultAsync<string>("fetchVersionJson", $"/version.json?t={cacheBuster}");

            if (!string.IsNullOrEmpty(fetchResult))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(fetchResult);
                var root = doc.RootElement;

                return new VersionInfo
                {
                    Version = root.TryGetProperty("version", out var version) ? version.GetString() : null,
                    BuildDate = root.TryGetProperty("buildDate", out var buildDate) ? buildDate.GetString() : null,
                    Commit = root.TryGetProperty("commit", out var commit) ? commit.GetString() : null,
                    Branch = root.TryGetProperty("branch", out var branch) ? branch.GetString() : null
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"서버 버전 정보 가져오기 실패: {ex.Message}");
        }

        return null;
    }

    private Task ShowGentleUpdateNotificationAsync()
    {
        if (_pendingUpdate is null)
            return Task.CompletedTask;

        _showUpdateNotification = true;
        StateHasChanged();

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

    private async Task DismissUpdateNotification()
    {
        if (_pendingUpdate is not null)
        {
            var dismissedVersions = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-dismissed-versions");
            var dismissedVersionsList = string.IsNullOrEmpty(dismissedVersions)
                ? new List<string>()
                : System.Text.Json.JsonSerializer.Deserialize<List<string>>(dismissedVersions) ?? new List<string>();

            if (!dismissedVersionsList.Contains(_pendingUpdate.Version ?? ""))
            {
                dismissedVersionsList.Add(_pendingUpdate.Version ?? "");

                if (dismissedVersionsList.Count > 5)
                    dismissedVersionsList.RemoveRange(0, dismissedVersionsList.Count - 5);

                var dismissedVersionsJson = System.Text.Json.JsonSerializer.Serialize(dismissedVersionsList);
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "tablecloth-dismissed-versions", dismissedVersionsJson);
            }
        }

        _showUpdateNotification = false;
        StateHasChanged();
    }

    private async Task ApplyUpdateAsync()
    {
        if (_pendingUpdate is null) return;

        await JSRuntime.InvokeVoidAsync("localStorage.setItem", "tablecloth-version", _pendingUpdate.Version);
        await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "tablecloth-dismissed-versions");
        await SafeInvokeJSAsync("window.forceRefresh");
    }

    private void StartPeriodicVersionCheck()
    {
        _updateCheckTimer?.Dispose();

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
                        !_showUpdateNotification)
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

        _updateCheckTimer.Change(TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    // ===== JS 상호작용 헬퍼 =====

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
        catch (Exception ex)
        {
            Console.WriteLine($"JavaScript 호출 실패 '{identifier}': {ex.Message}");
        }
    }

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

    public void Dispose()
    {
        SandboxService.ShowWsbDownloadGuideRequested -= OnShowWsbDownloadGuideRequested;
        _updateCheckTimer?.Dispose();
        dotNetHelper?.Dispose();
    }
}
