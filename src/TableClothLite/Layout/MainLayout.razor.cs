using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using TableClothLite.Components.Chat;
using TableClothLite.Components.Settings;
using TableClothLite.Components.Catalog;

namespace TableClothLite.Layout;

public partial class MainLayout : LayoutComponentBase
{
    private bool _showServiceModal = false;
    private bool _showSettingsModal = false;

    public void OpenServicesPage()
    {
        NavigationManager.NavigateTo("/services");
    }

    public Task OpenServicesModalAsync()
    {
        _showServiceModal = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    public Task OpenSettingDialog()
    {
        _showSettingsModal = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task CloseServiceModal()
    {
        _showServiceModal = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task CloseSettingsModal()
    {
        _showSettingsModal = false;
        StateHasChanged();
        return Task.CompletedTask;
    }
}
