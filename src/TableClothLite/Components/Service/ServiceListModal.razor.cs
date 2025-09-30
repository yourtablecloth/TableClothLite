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

    private IEnumerable<IGrouping<string, ServiceInfo>> FilteredServiceGroup { get; set; } = 
        Enumerable.Empty<IGrouping<string, ServiceInfo>>();

    private string _searchText = string.Empty;
    private string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            FilterServices();
        }
    }

    protected override void OnInitialized()
    {
        // ���� īŻ�αװ� �̹� �ε�Ǿ� �ִ��� Ȯ��
        if (Model.Services.Any())
        {
            ServiceGroup = Model.Services.GroupBy(x => x.Category.Trim().ToLowerInvariant());
            FilteredServiceGroup = ServiceGroup;
        }
        else
        {
            // īŻ�α� �ε�
            Model.LoadCatalogCommand.ExecuteAsync(this)
                .ContinueWith(async (task) => {
                    ServiceGroup = Model.Services.GroupBy(x => x.Category.Trim().ToLowerInvariant());
                    FilteredServiceGroup = ServiceGroup;
                    await InvokeAsync(StateHasChanged);
                });
        }
    }

    private void FilterServices()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredServiceGroup = ServiceGroup;
        }
        else
        {
            var searchTerms = SearchText.ToLowerInvariant();
            
            FilteredServiceGroup = ServiceGroup
                .Select(group => new
                {
                    Key = group.Key,
                    Services = group.Where(service =>
                        service.DisplayName.ToLowerInvariant().Contains(searchTerms) ||
                        service.ServiceId.ToLowerInvariant().Contains(searchTerms) ||
                        Model.DisplayCategoryName(group.Key).ToLowerInvariant().Contains(searchTerms))
                })
                .Where(group => group.Services.Any())
                .Select(group => group.Services.GroupBy(s => group.Key).First())
                .ToList();
        }
        
        StateHasChanged();
    }

    private void ClearSearch()
    {
        SearchText = string.Empty;
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