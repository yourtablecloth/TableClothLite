using KristofferStrube.Blazor.FileSystem;
using KristofferStrube.Blazor.FileSystemAccess;
using TG.Blazor.IndexedDB;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using TableClothLite.Services;
using TableClothLite.ViewModels;
using TableClothLite.Models;

namespace TableClothLite.Components.Setting;
public partial class Setting : ComponentBase, IDialogContentComponent
{
    [Inject]
    SandboxSettingsViewModel Model { get; set; } = default!;
    [Inject]
    ConfigService ConfigService { get; set; } = default!;
    [Inject]
    IFileSystemAccessService FileSystemAccessService { get; set; } = default!;
    [Inject]
    IndexedDBManager IndexedDbManger { get; set; } = default!;

    [CascadingParameter]
    public FluentDialog? Dialog { get; set; } = default!;
    private string? _currentPath;

	protected override async Task OnInitializedAsync()
	{
		var config = await ConfigService.LoadAsync();
		Model.ImportFromSandboxConfig(config);
	}
    
    protected async Task OnScanCertsButtonClick()
    {
        if (!(await FileSystemAccessService.IsSupportedAsync()))
        {
            _currentPath = "이 브라우저에서는 파일 시스템에 접근할 수 있는 기능이 제공되지 않습니다.";
            return;
        }

        var directoryHandle = await FileSystemAccessService.ShowDirectoryPickerAsync(new DirectoryPickerOptionsStartInWellKnownDirectory()
		{
			StartIn = WellKnownDirectory.Downloads,
		});

        var collection = new Dictionary<string, FileSystemFileHandle>(
            StringComparer.OrdinalIgnoreCase);

        _currentPath = "공동인증서 파일을 검색 중입니다.";
        StateHasChanged();

        await CollectCertificatesAsync(directoryHandle, collection);

        _currentPath = $"숨김 파일을 제외하고 총 {collection.Count}개 파일을 확인했습니다.";
        StateHasChanged();

        var foundCertCount = 0;
        foreach (var eachCertFile in collection)
        {
            if (!eachCertFile.Key.EndsWith("signCert.der", StringComparison.OrdinalIgnoreCase))
                continue;

            var signPriKey = string.Join('/', eachCertFile.Key
                .Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries)[0..^1]
                .Concat(new string[] { "signPri.key" }));

            if (!collection.TryGetValue(signPriKey, out var privateFileHandle))
                continue;

            await using var certFile = await eachCertFile.Value.GetFileAsync();
            var certFileContent = await certFile.ArrayBufferAsync();

            await using var privateFile = await privateFileHandle.GetFileAsync();
            var privateFileContent = await privateFile.ArrayBufferAsync();

            await IndexedDbManger.AddRecord(new StoreRecord<CertPairModel>()
            {
                Data = new CertPairModel(eachCertFile.Key, certFileContent, privateFileContent),
				Storename = "certificates",
            });
            foundCertCount++;

            await eachCertFile.Value.DisposeAsync();
        }

        _currentPath = $"숨김 파일을 제외하고 총 {foundCertCount}개 공동인증서를 웹 브라우저 내부 DB에 저장했습니다.";
        StateHasChanged();

        if (directoryHandle != null)
            await directoryHandle.DisposeAsync();
    }
    private async Task CollectCertificatesAsync(
        FileSystemDirectoryHandle directoryHandle,
        Dictionary<string, FileSystemFileHandle> list,
        Stack<string>? parentNames = default)
    {
        try
        {
            if (directoryHandle == null)
                return;

            if (parentNames == null)
            {
                parentNames = new Stack<string>();
                parentNames.Push(await directoryHandle.GetNameAsync());
            }

            var entries = await directoryHandle.ValuesAsync();

            foreach (var entry in entries)
            {
                if (entry is FileSystemDirectoryHandle subDirectoryHandle)
                {
                    parentNames.Push(await entry.GetNameAsync());
                    await CollectCertificatesAsync(subDirectoryHandle, list, parentNames);
                    await subDirectoryHandle.DisposeAsync();
                    continue;
                }

                if (entry is FileSystemFileHandle fileHandle)
                {
                    var directoryName = string.Join('/', parentNames.ToArray().Reverse().Skip(1));
                    var fileName = await fileHandle.GetNameAsync();
                    var relPath = string.Join('/', directoryName, fileName);

                    if (fileName.EndsWith(".der", StringComparison.OrdinalIgnoreCase) ||
                        fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
                        fileName.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(relPath, fileHandle);
                        continue;
                    }
                    await fileHandle.DisposeAsync();
                }
            }
        }
		catch (Exception ex)
		{
			Console.WriteLine(ex);
		}
        finally
        {
            parentNames?.Pop();
        }
    }

    private async Task SaveAsync()
    {
		var model = Model.ExportToSandboxConfig();
        await ConfigService.SaveAsync(model);
        await Dialog!.CloseAsync();
    }

    private async Task CancelAsync() => await Dialog!.CloseAsync();
}