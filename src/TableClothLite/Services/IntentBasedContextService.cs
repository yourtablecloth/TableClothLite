using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAI;
using OpenAI.Chat;

namespace TableClothLite.Services;

/// <summary>
/// 사용자 의도를 파악하고 관련 사이트 정보를 동적으로 주입하는 서비스
/// </summary>
public class IntentBasedContextService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private List<SiteInfo>? _cachedSites;
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(1);

    public IntentBasedContextService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// 전체 사이트 목록 로드 (캐시 활용)
    /// </summary>
    private async Task<List<SiteInfo>> LoadAllSitesAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedSites != null && (DateTime.UtcNow - _lastCacheUpdate) < CacheExpiration)
            return _cachedSites;

        var httpClient = _httpClientFactory.CreateClient();
        var url = "https://raw.githubusercontent.com/yourtablecloth/TableClothCatalog/main/docs/sites.json";
        
        try
        {
            var json = await httpClient.GetStringAsync(url, cancellationToken);
            _cachedSites = JsonSerializer.Deserialize<SiteList>(json)?.Sites ?? new List<SiteInfo>();
            _lastCacheUpdate = DateTime.UtcNow;
            Console.WriteLine($"✅ 사이트 목록 로드 완료: {_cachedSites.Count}개");
            return _cachedSites;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 사이트 목록 로드 실패: {ex.Message}");
            // 실패 시 빈 목록 반환
            return new List<SiteInfo>();
        }
    }

    /// <summary>
    /// 1단계: 사용자 의도 분석 - 사이트 추천이 필요한지 판단
    /// </summary>
    public async Task<IntentAnalysisResult> AnalyzeUserIntentAsync(
        OpenAIClient client,
        string userMessage,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        var chatClient = client.GetChatClient(modelName);

        var intentAnalysisPrompt = AiSystemPrompts.IntentAnalysisPrompt + userMessage;

        var messages = new[]
        {
            ChatMessage.CreateSystemMessage(intentAnalysisPrompt)
        };

        try
        {
            var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            var content = response.Value.Content[0].Text.Trim();

            // JSON 파싱
            content = content.Replace("```json", "").Replace("```", "").Trim();
            var result = JsonSerializer.Deserialize<IntentAnalysisResult>(content);

            if (result != null)
            {
                Console.WriteLine($"🔍 의도 분석 결과: needsSiteInfo={result.NeedsSiteInfo}, domains={string.Join(", ", result.Domains ?? new List<string>())}");
                return result;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ 의도 분석 실패: {ex.Message}");
        }

        // 기본값: 사이트 정보 불필요
        return new IntentAnalysisResult
        {
            NeedsSiteInfo = false,
            Domains = new List<string>(),
            Reason = "기본 모드로 진행"
        };
    }

    /// <summary>
    /// 2단계: 추출된 도메인과 일치하는 사이트 URL 찾기
    /// </summary>
    public async Task<List<SiteInfo>> FindMatchingSitesAsync(
        List<string> domains,
        CancellationToken cancellationToken = default)
    {
        var allSites = await LoadAllSitesAsync(cancellationToken);
        Console.WriteLine("추론된 도메인 목록: " + string.Join(", ", domains));

        if (!domains.Any())
            return new List<SiteInfo>();

        var matchedSites = allSites
            .Where(site => domains.Any(domain =>
                site.Url.Contains(domain, StringComparison.OrdinalIgnoreCase) ||
                site.Domain?.Contains(domain, StringComparison.OrdinalIgnoreCase) == true ||
                site.DisplayName.Contains(domain, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Console.WriteLine($"🎯 매칭된 사이트: {matchedSites.Count}개");
        foreach (var site in matchedSites)
        {
            Console.WriteLine($"  - {site.DisplayName} ({site.Url})");
        }

        return matchedSites;
    }

    /// <summary>
    /// 3단계: 매칭된 사이트 정보를 프롬프트에 주입
    /// </summary>
    public string BuildContextualPrompt(string originalMessage, List<SiteInfo> matchedSites)
    {
        if (!matchedSites.Any())
            return originalMessage;

        var contextBuilder = new StringBuilder();
        contextBuilder.Append(AiSystemPrompts.ContextualPromptHeader);

        foreach (var site in matchedSites)
        {
            contextBuilder.AppendLine($"### {site.DisplayName}");
            contextBuilder.AppendLine($"- **대표 URL**: {site.Url}");

            if (!string.IsNullOrEmpty(site.Category))
                contextBuilder.AppendLine($"- **카테고리**: {site.Category}");

            if (site.Subpages?.Any() == true)
            {
                contextBuilder.AppendLine("- **주요 서비스 페이지**:");
                foreach (var subpage in site.Subpages)
                {
                    contextBuilder.AppendLine($"  - **{subpage.Name}**: `{subpage.Url}`");
                    if (!string.IsNullOrEmpty(subpage.Description))
                        contextBuilder.AppendLine($"    - {subpage.Description}");
                }
            }

            contextBuilder.AppendLine();
        }

        contextBuilder.AppendLine(AiSystemPrompts.ContextualPromptFooter);
        contextBuilder.AppendLine();
        contextBuilder.AppendLine($"**사용자 질문**: {originalMessage}");

        return contextBuilder.ToString();
    }
}

/// <summary>
/// 의도 분석 결과
/// </summary>
public record IntentAnalysisResult
{
    [JsonPropertyName("needsSiteInfo")]
    public bool NeedsSiteInfo { get; init; }

    [JsonPropertyName("domains")]
    public List<string>? Domains { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>
/// 사이트 정보 모델
/// </summary>
public record SiteInfo
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("subpages")]
    public List<SubpageInfo>? Subpages { get; init; }
}

public record SubpageInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public record SiteList
{
    [JsonPropertyName("sites")]
    public List<SiteInfo> Sites { get; init; } = new();
}
