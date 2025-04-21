using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using TableClothLite.Components.Setting;
using TableClothLite.Services;

namespace TableClothLite.Layout;

public partial class MainLayout: LayoutComponentBase
{
    [Inject]
    IDialogService DialogService { get; set; } = default!;

    [Inject]
    OpenRouterAuthService AuthService { get; set; } = default!;

    [Inject]
    NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    IJSRuntime JSRuntime { get; set; } = default!;

    public async Task OpenChatPage()
    {
        var apiKey = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "openRouterApiKey");

        if (string.IsNullOrEmpty(apiKey))
            await AuthService.StartAuthFlowAsync();
        else
            NavigationManager.NavigateTo("/Chat");
    }

    public async Task OpenSettingDialog()
    {
        await DialogService.ShowDialogAsync<Setting>(
            new DialogParameters()
            {
                PreventScroll = true
            }
        );
    }
}