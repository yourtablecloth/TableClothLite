using OpenAI.Chat;
using OpenAI;
using System.ClientModel.Primitives;
using System.ClientModel;
using System.Text;
using System.Runtime.CompilerServices;

namespace TableClothLite.Services;

public sealed class OpenAIChatService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigService _configService;
    private readonly IntentBasedContextService _contextService;
    private readonly Dictionary<string, List<ChatMessage>> _conversationHistory = new();

    // 멀티턴 처리 상태 변경 이벤트
    public event EventHandler<ProcessingStatusEventArgs>? ProcessingStatusChanged;

    public OpenAIChatService(
        IHttpClientFactory httpClientFactory, 
        ConfigService configService,
        IntentBasedContextService contextService)
    {
        _httpClientFactory = httpClientFactory;
        _configService = configService;
        _contextService = contextService;
    }

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

    /// <summary>
    /// 멀티 턴 프롬프트를 활용한 스트리밍 메시지 전송
    /// </summary>
    public async IAsyncEnumerable<string> SendMessageStreamingAsync(
        OpenAIClient client, string message, string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 세션이 없으면 생성
        if (!_conversationHistory.ContainsKey(sessionId))
            await CreateNewSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);

        // 설정에서 모델 가져오기
        var config = await _configService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var chatClient = client.GetChatClient(config.OpenRouterModel);
        Console.WriteLine($"🤖 사용 모델: {config.OpenRouterModel}");

        string finalMessage = message;

        // === 멀티 턴 프롬프트 전략 시작 ===
        try
        {
            Console.WriteLine("🔍 1단계: 사용자 의도 분석 중...");
            RaiseProcessingStatus("자료 조사가 필요할지 생각하고 있습니다...");
            
            // 1단계: 사용자 의도 분석
            var intentResult = await _contextService.AnalyzeUserIntentAsync(
                client, message, config.OpenRouterModel, cancellationToken);

            // 사이트 정보가 필요한 경우에만 멀티 턴 프롬프트 적용
            if (intentResult.NeedsSiteInfo && intentResult.Domains?.Any() == true)
            {
                Console.WriteLine($"✅ 사이트 정보 필요: {string.Join(", ", intentResult.Domains)}");
                Console.WriteLine("🔍 2단계: 매칭되는 사이트 검색 중...");
                RaiseProcessingStatus("관련 정보를 조사하고 있습니다...");

                // 2단계: 매칭되는 사이트 찾기
                var matchedSites = await _contextService.FindMatchingSitesAsync(
                    intentResult.Domains, cancellationToken);

                if (matchedSites.Any())
                {
                    Console.WriteLine($"✅ {matchedSites.Count}개 사이트 매칭 완료");
                    Console.WriteLine("🔍 3단계: 컨텍스트 프롬프트 구성 중...");
                    RaiseProcessingStatus("정확한 답변을 준비하고 있습니다...");

                    // 3단계: 컨텍스트가 풍부한 프롬프트 생성
                    finalMessage = _contextService.BuildContextualPrompt(message, matchedSites);
                    Console.WriteLine("✅ 멀티 턴 프롬프트 적용 완료");
                }
                else
                {
                    Console.WriteLine("⚠️ 매칭되는 사이트 없음 - 기본 모드로 진행");
                }
            }
            else
            {
                Console.WriteLine($"ℹ️ 기본 모드로 진행: {intentResult.Reason}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ 멀티 턴 프롬프트 처리 중 오류: {ex.Message}");
            Console.WriteLine("ℹ️ 기본 모드로 진행합니다.");
            // 오류 발생 시 원본 메시지 사용
            finalMessage = message;
        }
        // === 멀티 턴 프롬프트 전략 종료 ===

        // 사용자 메시지 추가 (최종 메시지 사용)
        _conversationHistory[sessionId].Add(ChatMessage.CreateUserMessage(finalMessage));

        // AI 응답 대기 중 상태 표시
        Console.WriteLine("🤖 AI 응답 대기 중...");
        RaiseProcessingStatus("생각 중입니다...");

        // 대화 기록 전체를 넘겨 컨텍스트 유지
        var responseBuilder = new StringBuilder();
        bool isFirstChunk = true;

        await foreach (var completionUpdate in chatClient.CompleteChatStreamingAsync(
            _conversationHistory[sessionId].ToArray(),
            GetChatCompletionOptions(), cancellationToken).ConfigureAwait(false))
        {
            if (completionUpdate.ContentUpdate.Count < 1)
                continue;

            // 첫 번째 청크를 받으면 상태 메시지 제거
            if (isFirstChunk)
            {
                RaiseProcessingStatus(null);
                isFirstChunk = false;
            }

            // 텍스트 청크를 즉시 반환하여 UI에 즉각 렌더링
            var textChunk = completionUpdate.ContentUpdate[0].Text;

            if (!string.IsNullOrEmpty(textChunk))
            {
                responseBuilder.Append(textChunk);
                yield return textChunk;

                // 렌더링이 너무 빨라 UI 업데이트가 놓치지 않도록 아주 짧은 딜레이 추가 (선택적)
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
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

        return [
            ChatMessage.CreateSystemMessage(systemPrompt + AiSystemPrompts.ConstraintsAndSafetyGuidelines),
        ];
    }

    private ChatCompletionOptions GetChatCompletionOptions()
    {
        var options = new ChatCompletionOptions() { };
        return options;
    }

    private void RaiseProcessingStatus(string? status)
    {
        ProcessingStatusChanged?.Invoke(this, new ProcessingStatusEventArgs(status));
    }
}

/// <summary>
/// 처리 상태 변경 이벤트 인자
/// </summary>
public class ProcessingStatusEventArgs : EventArgs
{
    public string? Status { get; }
    
    public ProcessingStatusEventArgs(string? status)
    {
        Status = status;
    }
}
