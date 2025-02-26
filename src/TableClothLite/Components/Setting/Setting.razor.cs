using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using TableClothLite.Services;
using TableClothLite.ViewModels;

namespace TableClothLite.Components.Setting;
public partial class Setting : ComponentBase, IDialogContentComponent
{
    [Inject]
    SandboxSettingsViewModel Model { get; set; } = default!;
    [Inject]
    ConfigService ConfigService { get; set; } = default!;

    [CascadingParameter]
    public FluentDialog? Dialog { get; set; } = default!;

	protected override async Task OnInitializedAsync()
	{
		var config = await ConfigService.LoadAsync();
		Model.ImportFromSandboxConfig(config);
	}
    private async Task SaveAsync()
    {
		var model = Model.ExportToSandboxConfig();
        await ConfigService.SaveAsync(model);
        await Dialog!.CloseAsync();
    }

    private async Task CancelAsync() => await Dialog!.CloseAsync();
}