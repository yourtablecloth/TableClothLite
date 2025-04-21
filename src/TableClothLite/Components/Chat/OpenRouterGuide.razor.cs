using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using TableClothLite.Services;

namespace TableClothLite.Components.Chat;

public partial class OpenRouterGuide : ComponentBase, IDialogContentComponent
{
    [Inject]
    private OpenRouterAuthService AuthService { get; set; } = default!;

    [CascadingParameter]
    public FluentDialog? Dialog { get; set; } = default!;

    private async Task ContinueAsync()
    {
        await Dialog!.CloseAsync(true);
    }

    private async Task CancelAsync()
    {
        await Dialog!.CloseAsync(false);
    }
}