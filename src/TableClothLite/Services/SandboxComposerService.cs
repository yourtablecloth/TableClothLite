using System.Text;
using System.Xml;
using TableClothLite.Shared.Models;

namespace TableClothLite.Services;

public sealed class SandboxComposerService
{
    public SandboxComposerService(
        ConfigService configService)
    {
        _configService = configService;
    }

    private readonly ConfigService _configService;

    public async Task<XmlDocument> CreateSandboxDocumentAsync(
        SandboxService sandboxService,
        string? targetUrl,
        ServiceInfo? serviceInfo,
        CancellationToken cancellationToken = default)
    {
        var model = await _configService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var doc = new XmlDocument();
        var configuration = doc.CreateElement("Configuration");
        {
            var gpu = doc.CreateElement("vGPU");
            gpu.InnerText = "Disable";
            configuration.AppendChild(gpu);

            var networking = doc.CreateElement("Networking");
            networking.InnerText = model.EnableNetworking ? "Enable" : "Disable";
            configuration.AppendChild(networking);

            var audioInput = doc.CreateElement("AudioInput");
            audioInput.InnerText = model.EnableAudioInput ? "Enable" : "Disable";
            configuration.AppendChild(audioInput);

            var videoInput = doc.CreateElement("VideoInput");
            videoInput.InnerText = model.EnableVideoInput ? "Enable" : "Disable";
            configuration.AppendChild(videoInput);

            var printerRedirection = doc.CreateElement("PrinterRedirection");
            printerRedirection.InnerText = model.EnablePrinterRedirection ? "Enable" : "Disable";
            configuration.AppendChild(printerRedirection);

            var clipboardRedirection = doc.CreateElement("ClipboardRedirection");
            clipboardRedirection.InnerText = model.EnableClipboardRedirection ? "Enable" : "Disable";
            configuration.AppendChild(clipboardRedirection);

            var logonCommand = doc.CreateElement("LogonCommand");
            var command = doc.CreateElement("Command");

            var commandLines = new List<string>();
            var url = "https://yourtablecloth.app/TableClothLite/assets/installer.txt";
            commandLines.Add($"[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072");

            if (serviceInfo != null)
                commandLines.Add($"Invoke-Command -ScriptBlock ([scriptblock]::Create([System.Text.Encoding]::UTF8.GetString((New-Object System.Net.WebClient).DownloadData('{url}')))) -ArgumentList @('{serviceInfo.ServiceId}', '{targetUrl}')");

            command.InnerText = string.Join(" ", [
                @"C:\Windows\System32\cmd.exe",
                "/c", "start",
                @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
                $"\"{string.Join(";", commandLines)}\""
            ]);

            logonCommand.AppendChild(command);
            configuration.AppendChild(logonCommand);
        }
        doc.AppendChild(configuration);

        return doc;
    }
}
