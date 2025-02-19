using System.Collections.ObjectModel;

namespace TableClothLite.Models;

public sealed record class ServiceInfo(
    string ServiceId, string DisplayName, string Category,
    string Url, string CompatNotes)
{
    public ObservableCollection<PackageInfo> Packages { get; } = new ObservableCollection<PackageInfo>();

    public string ImageRelativePath => $"images/{Category}/{ServiceId}.png";
}
