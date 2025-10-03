namespace TableClothLite.Shared.Models;

public sealed record class SandboxConfig
{
    public bool EnableNetworking { get; init; } = true;
    public bool EnableAudioInput { get; init; } = false;
    public bool EnableVideoInput { get; init; } = false;
    public bool EnablePrinterRedirection { get; init; } = true;
    public bool EnableClipboardRedirection { get; init; } = true;
    public string OpenRouterModel { get; init; } = Constants.DefaultOpenRouterModel;
}
