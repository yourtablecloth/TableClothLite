using OpenAI.Chat;
using OpenAI;
using System.ClientModel.Primitives;
using System.ClientModel;
using System.Text;
using System.Runtime.CompilerServices;

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

        var chatClient = client.GetChatClient("meta-llama/llama-4-maverick");

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
        var url = "https://raw.githubusercontent.com/yourtablecloth/TableClothLite/refs/heads/main/src/TableClothLite/wwwroot/assets/instruction.md";
        var httpClient = _httpClientFactory.CreateClient();
        var systemPrompt = await httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

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
