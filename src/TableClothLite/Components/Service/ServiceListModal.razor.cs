using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using TableClothLite.Shared.Models;
using TableClothLite.ViewModels;

namespace TableClothLite.Components.Service;

public partial class ServiceListModal : ComponentBase, IDialogContentComponent
{
    [CascadingParameter] public FluentDialog? Dialog { get; set; } = default!;

    public IEnumerable<IGrouping<string, ServiceInfo>> ServiceGroup { get; set; } = 
        Enumerable.Empty<IGrouping<string, ServiceInfo>>();

    protected override void OnInitialized()
    {
        // 서비스 카탈로그가 이미 로드되어 있는지 확인
        if (Model.Services.Any())
        {
            ServiceGroup = Model.Services.GroupBy(x => x.Category.Trim().ToLowerInvariant());
        }
        else
        {
            // 카탈로그 로드
            Model.LoadCatalogCommand.ExecuteAsync(this)
                .ContinueWith(async (task) => {
                    ServiceGroup = Model.Services.GroupBy(x => x.Category.Trim().ToLowerInvariant());
                    await InvokeAsync(StateHasChanged);
                });
        }
    }

    private async Task LaunchServiceAsync(ServiceInfo service)
    {
        try
        {
            await Model.GenerateSandboxDocumentAsync(string.Empty, service);
            await CloseAsync(); // 서비스 실행 후 모달 닫기
        }
        catch (Exception ex)
        {
            // 에러 처리 - 필요시 토스트 메시지나 다른 방식으로 알림
            Console.WriteLine($"서비스 실행 중 오류: {ex.Message}");
        }
    }

    private void OnCardHover(bool isHovering)
    {
        // 필요시 호버 효과 처리
    }

    private async Task CloseAsync()
    {
        await Dialog!.CloseAsync();
    }
}