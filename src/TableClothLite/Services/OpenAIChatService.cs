using OpenAI.Chat;
using OpenAI;
using System.ClientModel.Primitives;
using System.ClientModel;
using System.Text;
using System.Runtime.CompilerServices;

namespace TableClothLite.Services;

public sealed class OpenAIChatService
{
    public OpenAIChatService(IHttpClientFactory httpClientFactory, ConfigService configService)
    {
        _httpClientFactory = httpClientFactory;
        _configService = configService;
    }

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigService _configService;
    private readonly Dictionary<string, List<ChatMessage>> _conversationHistory = new();

    public OpenAIClient CreateOpenAIClient(string apiKey)
    {
        var credential = new ApiKeyCredential(apiKey);

        var url = new Uri("https://openrouter.ai/api/v1/", UriKind.Absolute);
        var transport = new HttpClientPipelineTransport(
            _httpClientFactory.CreateClient("OpenRouter"));
        var options = new OpenAIClientOptions()
        {
            Endpoint = url,
            Transport = transport,
        };

        return new OpenAIClient(credential, options);
    }

    public async Task CreateNewSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_conversationHistory.ContainsKey(sessionId))
        {
            var messages = await GetSystemMessagesAsync(cancellationToken).ConfigureAwait(false);
            _conversationHistory[sessionId] = [.. messages];
        }
    }

    public async IAsyncEnumerable<string> SendMessageStreamingAsync(
        OpenAIClient client, string message, string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 세션이 없으면 생성
        if (!_conversationHistory.ContainsKey(sessionId))
            await CreateNewSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);

        // 사용자 메시지 추가
        _conversationHistory[sessionId].Add(ChatMessage.CreateUserMessage(message));

        // 설정에서 모델 가져오기
        var config = await _configService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var chatClient = client.GetChatClient(config.OpenRouterModel);
        Console.WriteLine(config.OpenRouterModel);

        // 대화 기록 전체를 넘겨 컨텍스트 유지
        var responseBuilder = new StringBuilder();

        await foreach (var completionUpdate in chatClient.CompleteChatStreamingAsync(
            _conversationHistory[sessionId].ToArray(),
            GetChatCompletionOptions(), cancellationToken).ConfigureAwait(false))
        {
            if (completionUpdate.ContentUpdate.Count < 1)
                continue;

            // 텍스트 청크를 즉시 반환하여 UI에 즉각 렌더링
            var textChunk = completionUpdate.ContentUpdate[0].Text;

            if (!string.IsNullOrEmpty(textChunk))
            {
                responseBuilder.Append(textChunk);
                yield return textChunk;

                // 렌더링이 너무 빨라 UI 업데이트가 놓치지 않도록 아주 짧은 딜레이 추가 (선택적)
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        // 응답 메시지를 대화 기록에 추가
        _conversationHistory[sessionId].Add(ChatMessage.CreateAssistantMessage(responseBuilder.ToString()));

        // 대화 컨텍스트 크기가 너무 커지지 않도록 관리
        // 토큰 수 제한을 초과하지 않도록 처리
        if (_conversationHistory[sessionId].Count > 10)
        {
            // 시스템 메시지를 제외한 가장 오래된 메시지 2개 제거
            _conversationHistory[sessionId].RemoveRange(1, 2);
        }
    }

    // 세션 초기화
    public async Task ClearSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_conversationHistory.ContainsKey(sessionId))
            return;

        _conversationHistory[sessionId].Clear();
        var messages = await GetSystemMessagesAsync(cancellationToken).ConfigureAwait(false);
        _conversationHistory[sessionId].AddRange(messages);
    }

    private async Task<IEnumerable<ChatMessage>> GetSystemMessagesAsync(
        CancellationToken cancellationToken = default)
    {
        var url = $"https://raw.githubusercontent.com/yourtablecloth/TableClothCatalog/refs/heads/main/docs/instruction.md?ts={DateTime.UtcNow.Ticks}";
        var httpClient = _httpClientFactory.CreateClient();
        var systemPrompt = await httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

        // 추가 제약사항 및 안전 가이드라인
        var constraintsPrompt = @"
## 중요 제약사항 및 안전 가이드라인

### 1. 주제 범위 엄수
- **허용된 주제**: 금융 서비스, 공공기관 웹사이트, Windows Sandbox, 보안, ActiveX/플러그인, 식탁보 프로젝트 관련 주제만 다룹니다.
- **범위 외 질문 대응**: 위 주제와 무관한 질문(프로그래밍 일반, 게임, 엔터테인먼트, 개인적 상담 등)에는 정중하게 거절하고 허용된 주제로 유도합니다.

### 2. 응답 품질 기준
- **정확성**: 사실에 기반한 검증된 정보만 제공합니다.
- **불확실성 표현**: 확실하지 않은 정보는 명시적으로 불확실함을 표현합니다.
- **전문가 권고**: 법률, 재정, 의학 자문이 필요한 경우 반드시 전문가 상담을 권장합니다.

### 3. 엄격한 금지 사항
- **개인정보 보호**: 비밀번호, 계좌번호, 주민등록번호, 신용카드 정보 등 민감정보를 절대 요구하거나 언급하지 않습니다.
- **불법 행위**: 불법 행위를 조장하거나 안내하지 않습니다.
- **전문 자문**: 의학, 법률, 세무 자문을 제공하지 않습니다.

### 4. 응답 형식
- 한국어로 명확하고 친절하게 답변합니다.
- 복잡한 내용은 단계별로 나눠 설명합니다.
- 필요시 마크다운 형식(리스트, 강조, 코드 블록 등)을 활용합니다.

### 5. 범위 외 질문 대응 예시

**사용자**: ""파이썬으로 게임 만드는 법 알려줘""
**어시스턴트**: ""죄송합니다. 저는 금융 및 공공기관 웹사이트 이용과 Windows Sandbox에 특화된 AI 어시스턴트입니다. 다음과 같은 주제에 대해 도움을 드릴 수 있습니다:

- 인터넷 뱅킹 보안 및 안전한 이용 방법
- 공공기관(홈택스, 정부24 등) 웹사이트 이용 가이드
- Windows Sandbox를 활용한 안전한 웹 브라우징
- ActiveX 및 보안 플러그인 관련 문제 해결

이 중 궁금하신 내용이 있으신가요?""

**사용자**: ""건강 상담 좀 해줘""
**어시스턴트**: ""죄송합니다. 건강 관련 상담은 제 전문 분야가 아니며, 의학적 자문은 반드시 의료 전문가와 상담하셔야 합니다. 저는 금융 서비스, 공공기관 웹사이트, Windows Sandbox 관련 주제에 특화되어 있습니다. 이와 관련된 질문이 있으시면 기꺼이 도와드리겠습니다.""

### 6. 안전한 응답 생성
- 응답 전에 항상 주제 범위를 확인합니다.
- 민감한 정보가 포함될 가능성이 있으면 일반적인 설명으로 대체합니다.
- 사용자의 안전과 보안을 최우선으로 고려합니다.

이 가이드라인을 모든 대화에서 철저히 준수하여 안전하고 유용한 정보만 제공합니다.
";

        return [
            ChatMessage.CreateSystemMessage(systemPrompt + constraintsPrompt),
        ];
    }

    private ChatCompletionOptions GetChatCompletionOptions()
    {
        var options = new ChatCompletionOptions() { };
        return options;
    }
}
