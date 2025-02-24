namespace TableClothLite.Shared.Models;

public sealed record class SandboxConfig(
    bool EnableNetworking = true,
    bool EnableAudioInput = true,
    bool EnableVideoInput = true,
    bool EnablePrinterRedirection = true,
    bool EnableClipboardRedirection = true)
{
}
