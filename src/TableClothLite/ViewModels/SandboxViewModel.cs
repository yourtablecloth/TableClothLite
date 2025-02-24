using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TableClothLite.Shared.Models;
using TableClothLite.Services;
using TableClothLite.Shared.Services;

namespace TableClothLite.ViewModels;

public sealed partial class SandboxViewModel : ObservableObject
{
    public SandboxViewModel(
        ILogger<SandboxViewModel> logger,
        FileDownloadService fileDownloadService,
        SandboxComposerService sandboxComposerService,
        CatalogService catalogService)
    {
        _logger = logger;
        _fileDownloadService = fileDownloadService;
        _sandboxComposerService = sandboxComposerService;
        _catalogService = catalogService;
    }

    private readonly ILogger _logger;
    private readonly FileDownloadService _fileDownloadService;
    private readonly SandboxComposerService _sandboxComposerService;
    private readonly CatalogService _catalogService;

    [RelayCommand]
    private Task LoadCatalogAsync()
        => _catalogService.LoadCatalogDocumentAsync(Services);

    public async Task GenerateSandboxDocumentAsync(
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken = default)
    {
        var doc = await _sandboxComposerService.CreateSandboxDocumentAsync(this, serviceInfo, cancellationToken).ConfigureAwait(false);
        using var memStream = new MemoryStream();
        doc.Save(memStream);
        memStream.Position = 0L;

        await _fileDownloadService.DownloadFileAsync(
            memStream, $"{serviceInfo.ServiceId}.wsb", "application/xml",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public string CalculateAbsoluteUrl(string relativePath)
        => _catalogService.CalculateAbsoluteUrl(relativePath).AbsoluteUri;

    public string DisplayCategoryName(string? category)
        => (category?.ToLowerInvariant()?.Trim()) switch
        {
            "banking" => "은행",
            "financing" => "저축은행",
            "creditcard" => "신용카드",
            "education" => "교육",
            "security" => "증권",
            "insurance" => "보험",
            "government" => "정부",
            "other" => "기타",
            _ => category ?? string.Empty,
        };

    [ObservableProperty]
    private ObservableCollection<ServiceInfo> _services = new();
}
