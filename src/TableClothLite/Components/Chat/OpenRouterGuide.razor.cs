using Microsoft.AspNetCore.Components;

namespace TableClothLite.Components.Chat;

public partial class OpenRouterGuide : ComponentBase
{
    [Parameter] public EventCallback<bool> OnClose { get; set; }

    private async Task ContinueAsync()
    {
        await OnClose.InvokeAsync(true);
    }

    private async Task CancelAsync()
    {
        await OnClose.InvokeAsync(false);
    }
}
