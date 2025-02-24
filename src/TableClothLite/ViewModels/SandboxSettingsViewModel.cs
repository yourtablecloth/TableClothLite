using CommunityToolkit.Mvvm.ComponentModel;
using TableClothLite.Shared.Models;

namespace TableClothLite.ViewModels;

public sealed partial class SandboxSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _enableNetworking = true;

    [ObservableProperty]
    private bool _enableAudioInput = true;

    [ObservableProperty]
    private bool _enableVideoInput = true;

    [ObservableProperty]
    private bool _enablePrinterRedirection = true;

    [ObservableProperty]
    private bool _enableClipboardRedirection = true;

    public SandboxConfig ExportToSandboxConfig()
    {
        return new SandboxConfig
        {
            EnableNetworking = EnableNetworking,
            EnableAudioInput = EnableAudioInput,
            EnableVideoInput = EnableVideoInput,
            EnablePrinterRedirection = EnablePrinterRedirection,
            EnableClipboardRedirection = EnableClipboardRedirection
        };
    }

    public void ImportFromSandboxConfig(SandboxConfig config)
    {
        EnableNetworking = config.EnableNetworking;
        EnableAudioInput = config.EnableAudioInput;
        EnableVideoInput = config.EnableVideoInput;
        EnablePrinterRedirection = config.EnablePrinterRedirection;
        EnableClipboardRedirection = config.EnableClipboardRedirection;
    }
}
