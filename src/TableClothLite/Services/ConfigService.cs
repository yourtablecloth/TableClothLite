using Blazored.LocalStorage;
using TableClothLite.Shared.Models;

namespace TableClothLite.Services;

public sealed class ConfigService(
    ILocalStorageService Storage
)
{
    public async Task SaveAsync(SandboxConfig config, CancellationToken cancellationToken = default)
        => await Storage.SetItemAsync("sandbox_settings", config, cancellationToken).ConfigureAwait(false);

    public async Task<SandboxConfig> LoadAsync(CancellationToken cancellationToken = default)
        => (await Storage.GetItemAsync<SandboxConfig>("sandbox_settings", cancellationToken).ConfigureAwait(false)) ?? new SandboxConfig();
}
