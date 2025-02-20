using Microsoft.Extensions.Configuration;
using System.Collections.ObjectModel;
using System.Xml;
using TableClothLite.Shared.Models;

namespace TableClothLite.Shared.Services;

public class CatalogService
{
    public CatalogService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public Uri CalculateAbsoluteUrl(string relativePath)
    {
        if (!Uri.TryCreate(_configuration["TableClothCatalogBaseUrl"], UriKind.Absolute, out var tableClothCatalogBaseUrl) ||
            tableClothCatalogBaseUrl == null || !string.Equals(Uri.UriSchemeHttps, tableClothCatalogBaseUrl.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception("Catalog URL missing or not an HTTPS url.");
        }

        return new Uri(tableClothCatalogBaseUrl, new Uri(relativePath, UriKind.Relative));
    }

    private async Task<XmlDocument> LoadCatalogDocumentInternalAsync(CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient();
        var content = await httpClient.GetStringAsync(CalculateAbsoluteUrl("Catalog.xml")).ConfigureAwait(false);

        var xmlDocument = new XmlDocument();
        xmlDocument.LoadXml(content);
        return xmlDocument;
    }

    public async Task LoadCatalogDocumentAsync(
        IList<ServiceInfo> services,
        CancellationToken cancellationToken = default)
    {
        var document = await LoadCatalogDocumentInternalAsync(cancellationToken).ConfigureAwait(false);

        var nodeList = document.SelectNodes("/TableClothCatalog/InternetServices/Service");
        if (nodeList == null || nodeList.Count < 1)
            return;

        foreach (XmlNode eachServiceNode in nodeList)
        {
            var serviceNodeAttrib = eachServiceNode.Attributes;
            if (serviceNodeAttrib == null || serviceNodeAttrib.Count < 1)
                continue;

            var serviceId = serviceNodeAttrib["Id"]?.Value;
            var serviceDisplayName = serviceNodeAttrib["DisplayName"]?.Value;
            var serviceCategory = serviceNodeAttrib["Category"]?.Value;
            var serviceUrl = serviceNodeAttrib["Url"]?.Value;

            if (string.IsNullOrWhiteSpace(serviceId) || string.IsNullOrEmpty(serviceDisplayName) ||
                string.IsNullOrWhiteSpace(serviceUrl) || !Uri.TryCreate(serviceUrl, UriKind.Absolute, out var parsedServiceUrl) || parsedServiceUrl == null ||
                !string.Equals(Uri.UriSchemeHttps, parsedServiceUrl.Scheme, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(serviceCategory))
                serviceCategory = "Others";

            var serviceCompatNotes = eachServiceNode.SelectSingleNode("./CompatNotes")?.InnerText ?? string.Empty;
            var serviceInfo = new ServiceInfo(serviceId, serviceDisplayName, serviceCategory, serviceUrl, serviceCompatNotes);

            var packageNodeList = eachServiceNode.SelectNodes("./Packages/Package");
            if (packageNodeList == null || packageNodeList.Count < 1)
                continue;

            foreach (XmlNode eachPackage in packageNodeList)
            {
                var packageNodeAttrib = eachPackage.Attributes;
                if (packageNodeAttrib == null || packageNodeAttrib.Count < 1)
                    continue;

                var packageName = packageNodeAttrib["Name"]?.Value;
                var packageUrl = packageNodeAttrib["Url"]?.Value;

                if (string.IsNullOrWhiteSpace(packageName) || 
                    string.IsNullOrWhiteSpace(packageUrl) || !Uri.TryCreate(packageUrl, UriKind.Absolute, out var parsedPackageUrl) || parsedPackageUrl == null ||
                    !string.Equals(Uri.UriSchemeHttps, parsedPackageUrl.Scheme, StringComparison.OrdinalIgnoreCase))
                    continue;

                var packageArguments = packageNodeAttrib["Arguments"]?.Value ?? string.Empty;

                var packageInfo = new PackageInfo(packageName, parsedPackageUrl.AbsoluteUri, packageArguments);
                serviceInfo.Packages.Add(packageInfo);
            }

            services.Add(serviceInfo);
        }
    }
}
