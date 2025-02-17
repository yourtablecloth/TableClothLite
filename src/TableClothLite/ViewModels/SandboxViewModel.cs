using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text;
using System.Xml;
using TableClothLite.Services;

namespace TableClothLite.ViewModels;

public sealed partial class SandboxViewModel : ObservableObject
{
    public SandboxViewModel(
        ILogger<SandboxViewModel> logger,
        FileDownloadService fileDownloadService,
        SandboxComposerService sandboxComposerService)
    {
        _logger = logger;
        _fileDownloadService = fileDownloadService;
        _sandboxComposerService = sandboxComposerService;
    }

    private readonly ILogger _logger;
    private readonly FileDownloadService _fileDownloadService;
    private readonly SandboxComposerService _sandboxComposerService;

    private async Task DownloadSandboxDocumentAsync(string url)
    {
        var doc = _sandboxComposerService.CreateSandboxDocument(this, url);
        using var memStream = new MemoryStream();
        doc.Save(memStream);
        memStream.Position = 0L;

        await _fileDownloadService.DownloadFileAsync(
            memStream, "TableClothLite.wsb", "application/xml");
    }

    [RelayCommand]
    private Task WooriBankAsync()
        => DownloadSandboxDocumentAsync("https://www.wooribank.com");

    [RelayCommand]
    private Task ShinhanBankAsync()
        => DownloadSandboxDocumentAsync("https://www.shinhan.com");

    [RelayCommand]
    private Task KookminBankAsync()
        => DownloadSandboxDocumentAsync("https://www.kbstar.com");

    [RelayCommand]
    private Task HanaBankAsync()
        => DownloadSandboxDocumentAsync("https://www.kebhana.com");

    [RelayCommand]
    private Task NHInternetBankAsync()
        => DownloadSandboxDocumentAsync("https://banking.nonghyup.com");

    [ObservableProperty]
    private bool _enableVGPU = true;

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

    [ObservableProperty]
    private ObservableCollection<string> _selectedApplications = new();
}
