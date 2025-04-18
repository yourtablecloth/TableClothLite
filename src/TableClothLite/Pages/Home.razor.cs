using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TableClothLite.Shared.Models;

namespace TableClothLite.Pages;

public partial class Home : ComponentBase, IAsyncDisposable
{
    public IEnumerable<IGrouping<string, ServiceInfo>> ServiceGroup =
        Enumerable.Empty<IGrouping<string, ServiceInfo>>();

    public bool IsServiceSectionRendered = false;
    private IJSObjectReference? module;
    
    protected override void OnInitialized()
    {
        // 비동기로 하면 화면 상호작용이 막히기 때문에 OnInitializedAsync 방식에서 변경
        Model.LoadCatalogCommand.ExecuteAsync(this)
            .ContinueWith(async (task) => {
                ServiceGroup = Model.Services.GroupBy(x => x.Category.Trim().ToLowerInvariant());
                await InvokeAsync(StateHasChanged);
            });
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./Pages/Home.razor.js");

        if (!IsServiceSectionRendered)
        {
            var initObserverResult = await module!.InvokeAsync<bool>("initObserver");
            IsServiceSectionRendered = initObserverResult;
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    public async ValueTask DisposeAsync()
    {
        if(module is not null)
            await module.DisposeAsync();
    }
}
