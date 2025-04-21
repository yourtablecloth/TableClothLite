using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using TableClothLite.Components.Chat;
using TableClothLite.Components.Setting;

namespace TableClothLite.Layout;

public partial class MainLayout : LayoutComponentBase
{
    public async Task OpenChatPage()
    {
        var apiKey = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "openRouterApiKey");

        if (string.IsNullOrEmpty(apiKey))
        {
            // API 키가 없을 경우 안내 대화 상자 표시
            var result = await DialogService.ShowDialogAsync<OpenRouterGuide>(
                new DialogParameters()
                {
                    Title = "OpenRouter 계정 필요",
                    PreventScroll = true,
                    PrimaryAction = "계속하기",
                    SecondaryAction = "취소",
                    Width = "450px"
                });

            // 사용자가 계속하기를 선택한 경우에만 인증 플로우 시작
            if (await result.GetReturnValueAsync<bool>() == true)
                await AuthService.StartAuthFlowAsync();
        }
        else
        {
            NavigationManager.NavigateTo("/Chat");
        }
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
