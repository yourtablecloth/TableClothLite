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

        var intentAnalysisPrompt = @"
당신은 사용자의 질문 의도를 분석하는 전문가입니다.

**목표**: 사용자가 특정 금융기관/공공기관 웹사이트에 대한 정보를 원하는지 판단합니다.

**판단 기준**:
1. **사이트 정보 필요 (needsSiteInfo: true)**:
   - 특정 금융기관/공공기관 이름이 명확히 언급됨
   - 해당 사이트의 로그인, 서비스, 페이지 위치 등을 물어봄
   - 예: ""KB국민은행 로그인 방법"", ""홈택스 세금 신고 페이지"", ""신한은행 공인인증서 발급""

2. **사이트 정보 불필요 (needsSiteInfo: false)**:
   - 일반적인 개념 질문
   - 특정 사이트와 무관한 질문
   - 예: ""Windows Sandbox란?"", ""인터넷 뱅킹 보안 팁"", ""공인인증서가 뭐예요?""

**응답 형식 (JSON만 반환)**:
```json
{
  ""needsSiteInfo"": true/false,
  ""domains"": [""domain1.com"", ""domain2.go.kr""],
  ""reason"": ""판단 이유""
}
```

**사용자 질문**:
" + userMessage;

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
            Reason = "분석 실패 - 기본 모드로 진행"
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
        contextBuilder.AppendLine("## 📋 관련 사이트 상세 정보");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("아래는 사용자 질문과 관련된 웹사이트의 상세 정보입니다. 이 정보를 **우선적으로 참고**하여 정확한 URL과 서비스 위치를 안내해주세요.");
        contextBuilder.AppendLine();

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

        contextBuilder.AppendLine("---");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine($"**사용자 질문**: {originalMessage}");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("위 사이트 정보를 바탕으로 정확한 URL과 함께 친절하게 안내해주세요.");

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
