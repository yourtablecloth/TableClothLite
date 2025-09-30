using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TableClothLite.Pages;

public partial class AuthCallback
{
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        var uri = new Uri(NavigationManager.Uri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        var code = query["code"];
        if (string.IsNullOrEmpty(code))
        {
            _error = "인증 실패: 코드 전달이 되지 않았거나 인증이 도중에 취소되었습니다.";
            return;
        }

        try
        {
            var apiKey = await AuthService.ObtainApiKeyAsync(code);

            // Store API key in local storage
            await JSRuntime.InvokeVoidAsync("localStorage.setItem", "openRouterApiKey", apiKey);

            // Navigate to chat
            NavigationManager.NavigateTo("/");
        }
        catch (Exception ex)
        {
            _error = $"인증 오류: {ex.Message}";
        }
    }

    private Task RetryAuthentication()
    {
        return AuthService.StartAuthFlowAsync();
    }
}
