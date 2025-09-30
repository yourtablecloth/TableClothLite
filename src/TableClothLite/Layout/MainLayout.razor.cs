using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using TableClothLite.Components.Chat;
using TableClothLite.Components.Setting;
using TableClothLite.Components.Service;

namespace TableClothLite.Layout;

public partial class MainLayout : LayoutComponentBase
{
    public void OpenServicesPage()
    {
        NavigationManager.NavigateTo("/services");
    }

    public async Task OpenServicesModalAsync()
    {
        await DialogService.ShowDialogAsync<ServiceListModal>(
            new DialogParameters()
            {
                Title = "서비스 목록",
                PreventScroll = true,
                Width = "800px",
                Height = "600px"
            }
        );
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
