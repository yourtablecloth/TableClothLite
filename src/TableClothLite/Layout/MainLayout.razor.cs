using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using TableClothLite.Components.Chat;
using TableClothLite.Components.Setting;

namespace TableClothLite.Layout;

public partial class MainLayout : LayoutComponentBase
{
    public void OpenServicesPage()
    {
        NavigationManager.NavigateTo("/services");
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
