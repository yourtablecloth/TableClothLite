using AngleSharp.Html.Parser;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using OpenAI;
using System.Net;
using TableClothLite.Models;
using TableClothLite.Shared.Models;

namespace TableClothLite.Pages;

public partial class Chat : IDisposable
{
    public IEnumerable<IGrouping<string, ServiceInfo>> ServiceGroup =
        Enumerable.Empty<IGrouping<string, ServiceInfo>>();

    private DotNetObjectReference<Chat>? dotNetHelper;
    private string _sessionId = Guid.NewGuid().ToString();
    private List<ChatMessage> _messages = [];
    private string _userInput = string.Empty;
    private bool _isStreaming = false;
    private string _currentStreamedMessage = string.Empty;
    private OpenAIClient? _client;
    private MarkdownPipeline? _markdownPipeline;
    private HtmlParser _htmlParser = new HtmlParser();

    // 글자 수 제한 관련 변수
    private readonly int _maxInputLength = 1000; // 최대 글자 수 제한
    private readonly int _warningThreshold = 100; // 제한에 근접했다고 경고할 잔여 글자 수 기준
    private bool _isNearLimit => _userInput.Length > _maxInputLength - _warningThreshold;

    protected override void OnInitialized()
    {
        _markdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseBootstrap()
            .DisableHtml()
            .Build();

        Model.LoadCatalogCommand.ExecuteAsync(this)
            .ContinueWith(async (task) => {
                ServiceGroup = Model.Services.GroupBy(x => x.Category.Trim().ToLowerInvariant());
                await InvokeAsync(StateHasChanged);
            });
    }

    protected override async Task OnInitializedAsync()
    {
        // Check if we have an API key stored
        var apiKey = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "openRouterApiKey");

        if (string.IsNullOrEmpty(apiKey))
        {
            NavigationManager.NavigateTo("/");
            return;
        }

        if (_client == null)
            _client = ChatService.CreateOpenAIClient(apiKey);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            dotNetHelper = DotNetObjectReference.Create(this);
            await JSRuntime.InvokeVoidAsync("Helpers.setDotNetHelper", dotNetHelper);
        }

        await JSRuntime.InvokeVoidAsync("scrollToBottom", "messages");
    }

    // 입력 내용이 변경될 때 호출되는 메서드
    private void OnInputChange(ChangeEventArgs e)
    {
        var newValue = e.Value?.ToString() ?? string.Empty;

        // 최대 길이를 초과하는 경우 잘라내기
        if (newValue.Length > _maxInputLength)
            newValue = newValue.Substring(0, _maxInputLength);

        _userInput = newValue;
    }

    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(_userInput) || _isStreaming)
            return;

        var userMessage = new ChatMessage { Content = _userInput, IsUser = true };
        _messages.Add(userMessage);

        var input = _userInput;
        _userInput = string.Empty;
        _isStreaming = true;
        _currentStreamedMessage = string.Empty;
        StateHasChanged();

        try
        {
            if (_client == null)
                throw new InvalidOperationException("Client is not initialized.");

            await JSRuntime.InvokeVoidAsync("scrollToBottom", "messages");

            await foreach (var chunk in ChatService.SendMessageStreamingAsync(_client, input, _sessionId))
            {
                _currentStreamedMessage += chunk;
                StateHasChanged();
            }

            _messages.Add(new ChatMessage { Content = _currentStreamedMessage, IsUser = false });
        }
        catch (Exception ex)
        {
            _messages.Add(new ChatMessage
            {
                Content = $"오류가 발생했습니다: {ex.ToString()}",
                IsUser = false
            });
        }
        finally
        {
            _isStreaming = false;
            _currentStreamedMessage = string.Empty;
            StateHasChanged();
        }
    }

    protected async Task Logout()
    {
        await JSRuntime.InvokeAsync<string>("localStorage.setItem", "openRouterApiKey", string.Empty);
        NavigationManager.NavigateTo("/");
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && e.ShiftKey == false)
        {
            await SendMessage();
            // 입력 필드에 다시 포커스 맞추기
            await JSRuntime.InvokeVoidAsync("document.getElementById", "chatTextArea").AsTask();
        }
    }

    private MarkupString FormatMessage(string markdown)
    {
        var html = string.Empty;

        if (string.IsNullOrWhiteSpace(markdown))
            return (MarkupString)html;

        // 마크다운을 HTML로 변환
        html = Markdown.ToHtml(markdown, _markdownPipeline);
        var document = _htmlParser.ParseDocument(html);

        if (document.Body == null)
        {
            Console.Error.WriteLine("Cannot parse fragment element.");
            return (MarkupString)html;
        }

        foreach (var eachAnchorElem in document.QuerySelectorAll("a"))
        {
            var currentHref = (eachAnchorElem.GetAttribute("href") ?? string.Empty).Trim();
            eachAnchorElem.RemoveAttribute("href");
            eachAnchorElem.SetAttribute("onclick", $"window.Helpers.openSandbox('{currentHref}');");
            eachAnchorElem.SetAttribute("style", "font-weight: bold; cursor: pointer;");
            eachAnchorElem.InnerHtml = $"<button>{WebUtility.HtmlEncode(currentHref)}</button>";
        }

        html = document.Body.InnerHtml;

        // 줄바꿈이 적용되지 않은 부분 처리 (마크다운에서 처리되지 않은 줄바꿈)
        return (MarkupString)html;
    }

    private Task ResetConversation()
    {
        _messages.Clear();
        ChatService.ClearSession(_sessionId);
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable("OpenSandbox")]
    public async Task OpenSandboxAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri))
            parsedUri = null;

        var serviceInfo = default(ServiceInfo);
        if (parsedUri != null)
            serviceInfo = Model.Services.FirstOrDefault(x => x.Url.StartsWith(parsedUri.AbsoluteUri));

        var doc = await SandboxComposer.CreateSandboxDocumentAsync(
            Model, parsedUri?.AbsoluteUri, serviceInfo);
        using var memStream = new MemoryStream();
        doc.Save(memStream);
        memStream.Position = 0L;

        await FileDownloader.DownloadFileAsync(
            memStream, $"{serviceInfo?.ServiceId ?? "generated"}.wsb", "application/xml")
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        dotNetHelper?.Dispose();
    }
}
