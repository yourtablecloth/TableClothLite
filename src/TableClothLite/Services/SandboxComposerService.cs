using System.Text;
using System.Xml;
using TableClothLite.ViewModels;

namespace TableClothLite.Services;

public sealed class SandboxComposerService
{
    public XmlDocument CreateSandboxDocument(SandboxViewModel viewModel, string url)
    {
        var doc = new XmlDocument();
        var configuration = doc.CreateElement("Configuration");
        {
            var gpu = doc.CreateElement("vGPU");
            gpu.InnerText = viewModel.EnableVGPU ? "Enable" : "Disable";
            configuration.AppendChild(gpu);

            var networking = doc.CreateElement("Networking");
            networking.InnerText = viewModel.EnableNetworking ? "Enable" : "Disable";
            configuration.AppendChild(networking);

            var audioInput = doc.CreateElement("AudioInput");
            audioInput.InnerText = viewModel.EnableAudioInput ? "Enable" : "Disable";
            configuration.AppendChild(audioInput);

            var videoInput = doc.CreateElement("VideoInput");
            videoInput.InnerText = viewModel.EnableVideoInput ? "Enable" : "Disable";
            configuration.AppendChild(videoInput);

            var printerRedirection = doc.CreateElement("PrinterRedirection");
            printerRedirection.InnerText = viewModel.EnablePrinterRedirection ? "Enable" : "Disable";
            configuration.AppendChild(printerRedirection);

            var clipboardRedirection = doc.CreateElement("ClipboardRedirection");
            clipboardRedirection.InnerText = viewModel.EnableClipboardRedirection ? "Enable" : "Disable";
            configuration.AppendChild(clipboardRedirection);

            var logonCommand = doc.CreateElement("LogonCommand");
            var command = doc.CreateElement("Command");

            var commandLines = new List<string>
            {
                $"Start-Process -FilePath '{url}'"
            };

            var base64Content = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(string.Join("; ", commandLines)));
            command.InnerText = string.Join(" ", [
                @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
                @$"""[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; Invoke-Expression ([System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{base64Content}')))"""
            ]);

            logonCommand.AppendChild(command);
            configuration.AppendChild(logonCommand);
        }
        doc.AppendChild(configuration);

        return doc;
    }
}
