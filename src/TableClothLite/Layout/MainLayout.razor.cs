using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using TableClothLite.Components.Setting;

namespace TableClothLite.Layout;

public partial class MainLayout: LayoutComponentBase
{
    [Inject]
    IDialogService DialogService { get; set; } = default!;

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