using Blazored.LocalStorage;
using TableClothLite.Models;
using TableClothLite.Shared.Models;

namespace TableClothLite.Services;

public sealed class SettingsService
{
    private const string STORAGE_KEY = "sandbox_settings";
    private readonly ILocalStorageService _localStorage;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private SandboxSettingsModel? _cachedSettings;

    public event EventHandler<SandboxSettingsModel>? SettingsChanged;

    public SettingsService(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    /// <summary>
    /// 현재 설정을 가져옵니다. 캐시된 설정이 없으면 로컬 스토리지에서 로드합니다.
    /// </summary>
    public async Task<SandboxSettingsModel> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedSettings != null)
        {
            return _cachedSettings;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_cachedSettings != null)
            {
                return _cachedSettings;
            }

            var config = await _localStorage.GetItemAsync<SandboxConfig>(STORAGE_KEY, cancellationToken);
            _cachedSettings = new SandboxSettingsModel();
            
            if (config != null)
            {
                _cachedSettings.FromSandboxConfig(config);
            }

            return _cachedSettings;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 설정을 저장합니다.
    /// </summary>
    public async Task SaveSettingsAsync(SandboxSettingsModel settings, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var config = settings.ToSandboxConfig();
            await _localStorage.SetItemAsync(STORAGE_KEY, config, cancellationToken);
            
            _cachedSettings = settings;
            SettingsChanged?.Invoke(this, settings);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 설정을 업데이트합니다. 기존 설정을 로드하고 변경 사항을 적용한 후 저장합니다.
    /// </summary>
    public async Task UpdateSettingsAsync(Action<SandboxSettingsModel> updateAction, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        updateAction(settings);
        await SaveSettingsAsync(settings, cancellationToken);
    }

    /// <summary>
    /// 설정을 기본값으로 초기화합니다.
    /// </summary>
    public async Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var defaultSettings = new SandboxSettingsModel();
        await SaveSettingsAsync(defaultSettings, cancellationToken);
    }

    /// <summary>
    /// 캐시된 설정을 지우고 다시 로드합니다.
    /// </summary>
    public async Task RefreshSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _cachedSettings = null;
        }
        finally
        {
            _semaphore.Release();
        }

        await GetSettingsAsync(cancellationToken);
    }
}
