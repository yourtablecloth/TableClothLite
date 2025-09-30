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
        // ���� īŻ�αװ� �̹� �ε�Ǿ� �ִ��� Ȯ��
        if (Model.Services.Any())
        {
            ServiceGroup = Model.Services.GroupBy(x => x.Category.Trim().ToLowerInvariant());
        }
        else
        {
            // īŻ�α� �ε�
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
            await CloseAsync(); // ���� ���� �� ��� �ݱ�
        }
        catch (Exception ex)
        {
            // ���� ó�� - �ʿ�� �佺Ʈ �޽����� �ٸ� ������� �˸�
            Console.WriteLine($"���� ���� �� ����: {ex.Message}");
        }
    }

    private void OnCardHover(bool isHovering)
    {
        // �ʿ�� ȣ�� ȿ�� ó��
    }

    private async Task CloseAsync()
    {
        await Dialog!.CloseAsync();
    }
}