using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using TableClothLite.Shared.Models;
using TableClothLite.ViewModels;

namespace TableClothLite.Pages;

public partial class Home : ComponentBase, IAsyncDisposable
{
    public IEnumerable<IGrouping<string, ServiceInfo>> ServiceGroup;
    public bool IsServiceSectionRendered = false;
    private IJSObjectReference? module;
    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject]
    public SandboxViewModel Model { get; set; } = default!;
    
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
        if (firstRender) {
            module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./Pages/Home.razor.js");
        }
        if (!IsServiceSectionRendered) {
            var a = await module!.InvokeAsync<bool>("initObserver");
            IsServiceSectionRendered = a;
        }
        await base.OnAfterRenderAsync(firstRender);
    }

    public async ValueTask DisposeAsync()
    {
        if(module is not null) {
            await module.DisposeAsync();
        }
    }
}