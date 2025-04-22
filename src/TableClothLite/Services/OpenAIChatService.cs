using OpenAI.Chat;
using OpenAI;
using System.ClientModel.Primitives;
using System.ClientModel;
using System.Text;

namespace TableClothLite.Services;

public sealed class OpenAIChatService
{
    public OpenAIChatService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    private readonly IHttpClientFactory _httpClientFactory;
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

    public void CreateNewSession(string sessionId)
    {
        if (!_conversationHistory.ContainsKey(sessionId))
            _conversationHistory[sessionId] = [.. GetSystemMessages()];
    }

    public async IAsyncEnumerable<string> SendMessageStreamingAsync(
        OpenAIClient client, string message, string sessionId)
    {
        // 세션이 없으면 생성
        if (!_conversationHistory.ContainsKey(sessionId))
            CreateNewSession(sessionId);

        // 사용자 메시지 추가
        _conversationHistory[sessionId].Add(ChatMessage.CreateUserMessage(message));

        var chatClient = client.GetChatClient("meta-llama/llama-4-maverick");

        // 대화 기록 전체를 넘겨 컨텍스트 유지
        var responseBuilder = new StringBuilder();

        await foreach (var completionUpdate in chatClient.CompleteChatStreamingAsync(
            _conversationHistory[sessionId].ToArray(),
            GetChatCompletionOptions()))
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
                await Task.Delay(10);
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
    public void ClearSession(string sessionId)
    {
        if (!_conversationHistory.ContainsKey(sessionId))
            return;

        _conversationHistory[sessionId].Clear();
        _conversationHistory[sessionId].AddRange(GetSystemMessages());
    }

    private IEnumerable<ChatMessage> GetSystemMessages()
    {
        // TODO: 시스템 프롬프트를 외부에서 가져오도록 변경
        var systemPrompt =
            $$"""
            # Default Behavior

            You are a professional with extensive knowledge of financial services and public affairs in the Republic of Korea.
            When asked, proactively provide web page addresses that can lead users to the website of their bank, credit card company, insurance company, or government agency so they can find the information they want.
            If the user enter information that you suspect is personal information, refuse to answer.
            Please answer all responses in Korean only.

            ## Sitemap of Major Financial Institutions and Public Institutions in Korea

            When you need to guide users to online services of financial institutions and public institutions, you must provide the exact website address by referring to the information below. If you do not know the address, do not guess it yourself, but provide the website address of the institution instead.

            ### KB국민은행 (https://www.kbstar.com)

            개인 뱅킹 인증센터 - https://obank.kbstar.com/quics?page=C018872
            개인 뱅킹 조회 - https://obank.kbstar.com/quics?page=C016511
            개인 뱅킹 이체 - https://obank.kbstar.com/quics?page=C016524
            개인 뱅킹 공과금 - https://obank.kbstar.com/quics?page=C016526
            개인 뱅킹 금융상품 - https://obank.kbstar.com/quics?page=C030037
            개인 뱅킹 외환 - https://obank.kbstar.com/quics?page=C102239
            개인 뱅킹 관리 - https://obank.kbstar.com/quics?page=C016535
            """;

        return [
            ChatMessage.CreateSystemMessage(systemPrompt),
        ];
    }

    private ChatCompletionOptions GetChatCompletionOptions()
    {
        var options = new ChatCompletionOptions() { };
        return options;
    }
}
