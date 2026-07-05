using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using TableClothLite.Shared.Models;

namespace TableClothLite.Services;

/// <summary>
/// WebMCP(document/navigator.modelContext) 연동. 지원 브라우저에서 식탁보 카탈로그/무설치 .wsb 기능을
/// 에이전트가 호출할 수 있는 도구로 노출한다. 미지원 브라우저에서는 JS 쪽 feature-detect 가 조용히 폴백하며,
/// 이 서비스의 초기화 실패도 무음 처리한다(앱 동작에 영향 없음).
/// </summary>
public sealed class WebMcpInteropService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly SandboxService _sandbox;
    private DotNetObjectReference<WebMcpInteropService>? _ref;
    private bool _initialized;

    public WebMcpInteropService(IJSRuntime js, SandboxService sandbox)
    {
        _js = js;
        _sandbox = sandbox;
    }

    /// <summary>
    /// 지원 브라우저에서만 WebMCP 도구를 등록한다. 미지원/예외 시 조용히 무시(폴백).
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
            return;
        _initialized = true;

        try
        {
            _ref = DotNetObjectReference.Create(this);
            await _js.InvokeAsync<bool>("registerWebMcpTools", _ref);
        }
        catch
        {
            // JS 함수 부재/미지원/스펙 상이 등 어떤 이유로든 실패하면 폴백(무음).
        }
    }

    [JSInvokable]
    public async Task<string> WebMcp_SearchCatalog(string query)
    {
        await _sandbox.EnsureCatalogLoadedAsync();

        var q = (query ?? string.Empty).Trim();
        IEnumerable<ServiceInfo> results = _sandbox.Services;

        if (!string.IsNullOrEmpty(q))
        {
            var lower = q.ToLowerInvariant();
            results = results.Where(s =>
                s.DisplayName.ToLowerInvariant().Contains(lower) ||
                s.ServiceId.ToLowerInvariant().Contains(lower) ||
                _sandbox.DisplayCategoryName(s.Category).ToLowerInvariant().Contains(lower));
        }

        var list = results
            .OrderBy(s => s.Category)
            .ThenBy(s => s.DisplayName)
            .Take(50)
            .Select(s => $"- {s.DisplayName} (id: {s.ServiceId}, category: {_sandbox.DisplayCategoryName(s.Category)})")
            .ToArray();

        if (list.Length == 0)
            return string.IsNullOrEmpty(q)
                ? "The TableCloth catalog is empty or not loaded yet."
                : $"No TableCloth catalog sites matched '{q}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"{list.Length} site(s):");
        foreach (var line in list)
            sb.AppendLine(line);
        return sb.ToString().TrimEnd();
    }

    [JSInvokable]
    public async Task<string> WebMcp_GetSiteInfo(string siteId)
    {
        await _sandbox.EnsureCatalogLoadedAsync();

        var id = (siteId ?? string.Empty).Trim();
        var site = _sandbox.Services.FirstOrDefault(s =>
            string.Equals(s.ServiceId, id, StringComparison.OrdinalIgnoreCase));

        if (site is null)
            return $"No catalog site found with id '{siteId}'. Use tablecloth_search_sites to find valid ids.";

        var sb = new StringBuilder();
        sb.AppendLine($"Name: {site.DisplayName}");
        sb.AppendLine($"Id: {site.ServiceId}");
        sb.AppendLine($"Category: {_sandbox.DisplayCategoryName(site.Category)}");
        sb.AppendLine($"URL: {site.Url}");
        if (!string.IsNullOrWhiteSpace(site.CompatNotes))
            sb.AppendLine($"Notes: {site.CompatNotes}");
        return sb.ToString().TrimEnd();
    }

    [JSInvokable]
    public async Task<string> WebMcp_GenerateWsb(string siteId)
    {
        await _sandbox.EnsureCatalogLoadedAsync();

        var id = (siteId ?? string.Empty).Trim();
        ServiceInfo? site = null;

        if (!string.IsNullOrEmpty(id))
        {
            site = _sandbox.Services.FirstOrDefault(s =>
                string.Equals(s.ServiceId, id, StringComparison.OrdinalIgnoreCase));

            if (site is null)
                return JsonSerializer.Serialize(new
                {
                    fileName = string.Empty,
                    xml = string.Empty,
                    message = $"No catalog site with id '{siteId}'. Use tablecloth_search_sites first."
                });
        }

        var (fileName, xml) = await _sandbox.BuildWsbAsync(site);
        var target = site is null ? "the generic launcher" : $"'{site.DisplayName}'";
        var message =
            $"Prepared {fileName} for {target}. The .wsb download has been offered - the user should double-click " +
            "the downloaded file to launch it in Windows Sandbox. (The sandbox cannot be launched from the browser.)";

        return JsonSerializer.Serialize(new { fileName, xml, message });
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("unregisterWebMcpTools");
        }
        catch
        {
            // 무시(폴백)
        }
        _ref?.Dispose();
    }
}
