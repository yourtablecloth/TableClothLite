using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TableClothLite.Shared.Models;
using TableClothLite.Services;

namespace TableClothLite.Pages;

public partial class Home : ComponentBase, IAsyncDisposable
{
    [Inject] private SandboxService SandboxService { get; set; } = default!;
    
    public IEnumerable<IGrouping<string, ServiceInfo>> ServiceGroup =
        Enumerable.Empty<IGrouping<string, ServiceInfo>>();

    public bool IsServiceSectionRendered = false;
    private IJSObjectReference? module;
    
    protected override void OnInitialized()
    {
        // 비동기로 하면 화면 상호작용이 막히기 때문에 OnInitializedAsync 방식에서 변경
        SandboxService.LoadCatalogAsync()
            .ContinueWith(async (task) => {
                ServiceGroup = SandboxService.Services.GroupBy(x => x.Category.Trim().ToLowerInvariant());
                await InvokeAsync(StateHasChanged);
            });
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./Pages/Home.razor.js");

        if (module != null)
        {
            if (!IsServiceSectionRendered)
            {
                var initObserverResult = await module!.InvokeAsync<bool>("initObserver");
                IsServiceSectionRendered = initObserverResult;
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    // WSB 다운로드 가이드 모달 닫기
    private void CloseWsbDownloadGuide()
    {
        SandboxService.CloseWsbDownloadGuide();
        StateHasChanged();
    }

    // WSB 파일을 어쨌든 다운로드
    private async Task DownloadWsbAnyway()
    {
        await SandboxService.DownloadPendingFileAsync();
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
            await module.DisposeAsync();
    }
}
