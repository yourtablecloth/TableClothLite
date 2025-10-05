using TableClothLite.Shared.Models;

namespace TableClothLite.Models;

public sealed partial class SandboxSettingsModel
{
    public bool EnableNetworking { get; set; } = true;
    public bool EnableAudioInput { get; set; } = true;
    public bool EnableVideoInput { get; set; } = true;
    public bool EnablePrinterRedirection { get; set; } = true;
    public bool EnableClipboardRedirection { get; set; } = true;
    public string OpenRouterModel { get; set; } = Constants.DefaultOpenRouterModel;

    public SandboxConfig ToSandboxConfig()
    {
        return new SandboxConfig
        {
            EnableNetworking = EnableNetworking,
            EnableAudioInput = EnableAudioInput,
            EnableVideoInput = EnableVideoInput,
            EnablePrinterRedirection = EnablePrinterRedirection,
            EnableClipboardRedirection = EnableClipboardRedirection,
            OpenRouterModel = OpenRouterModel
        };
    }

    public void FromSandboxConfig(SandboxConfig config)
    {
        EnableNetworking = config.EnableNetworking;
        EnableAudioInput = config.EnableAudioInput;
        EnableVideoInput = config.EnableVideoInput;
        EnablePrinterRedirection = config.EnablePrinterRedirection;
        EnableClipboardRedirection = config.EnableClipboardRedirection;
        OpenRouterModel = config.OpenRouterModel;
    }
}
