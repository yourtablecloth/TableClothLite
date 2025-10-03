using AngleSharp.Html.Parser;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using OpenAI;
using System.Net;
using TableClothLite.Models;
using TableClothLite.Shared.Models;
using TableClothLite.Services;
using TableClothLite.Components;

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
    
    // API Ű ���� ����
    private bool _hasApiKey = false;
    private bool _isCheckingApiKey = true;

    // ���̵�� ���� ���� (�ʱⰪ�� false, ȭ�� ũ�⿡ ���� ���� ����)
    private bool _isSidebarOpen = false;
    private bool _isInitialized = false;

    // ���� �� ���� ���� ����
    private readonly int _maxInputLength = 1000; // �ִ� ���� �� ����
    private readonly int _warningThreshold = 100; // ���ѿ� �����ߴٰ� ����� �ܿ� ���� �� ����
    private bool _isNearLimit => _userInput.Length > _maxInputLength - _warningThreshold;

    // Dirty state ���� - ��ȭ ������ �ִ��� ����
    private bool _hasUnsavedContent => _messages.Any() || !string.IsNullOrWhiteSpace(_userInput);

    // �ʿ��� ���񽺵� inject
    [Inject] private OpenRouterAuthService AuthService { get; set; } = default!;

    // Windows Sandbox ���̵� ��� ���� ����
    private bool _showSandboxGuide = false;
    private bool _isWindowsOS = true;

    // ���� ��� ��� ���� ����
    private bool _showServicesModal = false;
    
    // ���� ��� ���� ����
    private bool _showSettingsModal = false;

    // ��ȭ �׼� ���downs ���� ����
    private bool _showConversationActionsDropdown = false;

    // �� ���� �˸� ���� ���� - confirm ��� �ξ� �˸� ���
    private bool _showUpdateNotification = false;
    private VersionInfo? _pendingUpdate = null;
    private Timer? _updateCheckTimer = null;

    // ModelIndicator ���۷���
    private ModelIndicator? _modelIndicator;
    
    // �Ŀ� ��� ����
    private bool _sponsorBannerDismissed = false;

    // ���� ���� Ŭ���� - ����ȭ
    private class VersionInfo
    {
        public string? Version { get; set; }
        public string? BuildDate { get; set; }
        public string? Commit { get; set; }
        public string? Branch { get; set; }
    }

    // JavaScript���� ��ȯ�� ���� ��� Ŭ����
    private class ShareResult
    {
        public bool Success { get; set; }
        public string Method { get; set; } = string.Empty;
        public string? Error { get; set; }
    }

    protected override void OnInitialized()
    {
        // ȣȯ���� ���� /Chat ��� �����̷�Ʈ ó��
        var uri = new Uri(NavigationManager.Uri);
        if (uri.AbsolutePath.Equals("/Chat", StringComparison.OrdinalIgnoreCase))
        {
            NavigationManager.NavigateTo("/", replace: true);
            return;
        }

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
        await CheckApiKeyStatus();
        await LoadSponsorBannerStatus();
    }
    
    private async Task LoadSponsorBannerStatus()
    {
        try
        {
            var dismissed = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "sponsor-banner-dismissed");
            _sponsorBannerDismissed = !string.IsNullOrEmpty(dismissed) && dismissed == "true";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"�Ŀ� ��� ���� �ε� �� ����: {ex.Message}");
            _sponsorBannerDismissed = false;
        }
    }

    private async Task CheckApiKeyStatus()
    {
        _isCheckingApiKey = true;
        StateHasChanged();

        try
        {
            // Check if we have an API key stored
            var apiKey = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "openRouterApiKey");
            _hasApiKey = !string.IsNullOrEmpty(apiKey);

            if (_hasApiKey)
            {
                if (_client == null)
                    _client = ChatService.CreateOpenAIClient(apiKey);
            }
            else
            {
                _client = null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"API Ű ���� Ȯ�� �� ����: {ex.Message}");
            _hasApiKey = false;
        }
        finally
        {
            _isCheckingApiKey = false;
            StateHasChanged();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            dotNetHelper = DotNetObjectReference.Create(this);
            
            // JavaScript �Լ����� �����ϰ� �ʱ�ȭ
            await InitializeJavaScriptAsync();
            
            // ĳ�� ��ȿȭ �� ���� üũ
            await CheckAppVersionAsync();
            
            // �ʱ� ȭ�� ũ�⿡ ���� ���̵�� ���� ����
            await InitializeSidebarState();
        }

        await SafeInvokeJSAsync("scrollToBottom", "messages");
    }

    // JavaScript �ʱ�ȭ�� �����ϰ� ó��
    private async Task InitializeJavaScriptAsync()
    {
        try
        {
            // �⺻ JavaScript �Լ����� �ε�� ������ ���
            var maxAttempts = 50; // 5�� ��� (100ms * 50)
            var attempts = 0;
            
            while (attempts < maxAttempts)
            {
                try
                {
                    // Helpers ��ü�� �����ϴ��� Ȯ��
                    var helpersExists = await JSRuntime.InvokeAsync<bool>("eval", "typeof window.Helpers !== 'undefined'");
                    if (helpersExists)
                    {
                        Console.WriteLine("JavaScript Helpers ��ü�� �غ�Ǿ����ϴ�.");
                        break;
                    }
                }
                catch
                {
                    // ��� �õ�
                }
                
                attempts++;
                await Task.Delay(100);
            }

            if (attempts >= maxAttempts)
            {
                Console.WriteLine("Warning: JavaScript Helpers ��ü�� ã�� �� �����ϴ�. �⺻ ��ɸ� ���˴ϴ�.");
                return;
            }

            // Helpers�� �غ�Ǹ� �ʱ�ȭ ����
            await SafeInvokeJSAsync("Helpers.setDotNetHelper", dotNetHelper);
            await SafeInvokeJSAsync("setupBeforeUnloadHandler", dotNetHelper);
            await SafeInvokeJSAsync("setupDropdownClickOutside", dotNetHelper);
            await SafeInvokeJSAsync("initChatInput");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JavaScript �ʱ�ȭ �� ����: {ex.Message}");
        }
    }

    // ������ JavaScript ȣ��
    private async Task SafeInvokeJSAsync(string identifier, params object[] args)
    {
        try
        {
            await JSRuntime.InvokeVoidAsync(identifier, args);
        }
        catch (JSException ex) when (ex.Message.Contains("undefined"))
        {
            Console.WriteLine($"JavaScript �Լ� '{identifier}'�� ���ǵ��� ����: {ex.Message}");
        }
        catch (JSException ex)
        {
            Console.WriteLine($"JavaScript ȣ�� ���� '{identifier}': {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"����ġ ���� ���� '{identifier}': {ex.Message}");
        }
    }

    // JavaScript���� ȣ���� �� �ִ� �޼��� - beforeunload �� unsaved content Ȯ��
    [JSInvokable]
    public bool HasUnsavedContent()
    {
        return _hasUnsavedContent;
    }

    // JavaScript���� ȣ���� �� �ִ� �޼��� - ��Ӵٿ� �����
    [JSInvokable]
    public Task HideConversationActionsDropdown()
    {
        _showConversationActionsDropdown = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    // JavaScript���� ȣ���� �� �ִ� �޼��� - ����ڽ����� ��ũ ����
    [JSInvokable]
    public async Task OpenSandbox(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                Console.WriteLine("URL�� ����ֽ��ϴ�.");
                return;
            }

            Console.WriteLine($"����ڽ����� URL ����: {url}");
            
            // SandboxViewModel�� ���� ����ڽ����� URL ����
            // URL�� �ִ� ��� �⺻ ���� ���� ����
            var defaultService = new ServiceInfo(
                ServiceId: "web-browser",
                DisplayName: "�� ������", 
                Category: "other",
                Url: url,
                CompatNotes: "AI ä�ÿ��� ������ ��ũ"
            );
            
            await Model.GenerateSandboxDocumentAsync(url, defaultService);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"����ڽ����� ��ũ ���� �� ����: {ex.Message}");
            
            // ���� �߻� �� ����ڿ��� �˸�
            await SafeInvokeJSAsync("showToast", 
                "����ڽ����� ��ũ�� �� �� �����ϴ�. Windows Sandbox�� ��ġ�Ǿ� �ִ��� Ȯ�����ּ���.", 
                "error");
        }
    }

    // JavaScript���� ȣ���� �� �ִ� �޼��� - �� ���� ���� (����ȭ�� ����)
    [JSInvokable]
    public async Task OnNewVersionDetected(string versionInfoJson)
    {
        try
        {
            if (string.IsNullOrEmpty(versionInfoJson)) return;
            
            using var doc = System.Text.Json.JsonDocument.Parse(versionInfoJson);
            var root = doc.RootElement;
            
            _pendingUpdate = new VersionInfo
            {
                Version = root.TryGetProperty("version", out var version) ? version.GetString() : null,
                BuildDate = root.TryGetProperty("buildDate", out var buildDate) ? buildDate.GetString() : null,
                Commit = root.TryGetProperty("commit", out var commit) ? commit.GetString() : null,
                Branch = root.TryGetProperty("branch", out var branch) ? branch.GetString() : null
            };
            
            // ��Ŀ�� ���ѱ� ���� �ε巯�� �ξ� �˸��� ǥ��
            await ShowGentleUpdateNotificationAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"�� ���� ���� ó�� �� ����: {ex.Message}");
        }
    }

    // �� ���� üũ �� ĳ�� ����
    private async Task CheckAppVersionAsync()
    {
        try
        {
            // ���� �� ���� (fallback��)
            const string FALLBACK_VERSION = "2024.12.17.1";
            
            // version.json���� ���� ���� Ȯ��
            var serverVersionInfo = await GetServerVersionAsync();
            
            // ���� ���丮������ ����� ���� Ȯ��
            var storedVersion = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-version");
            var dismissedVersions = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-dismissed-versions");
            var dismissedVersionsList = string.IsNullOrEmpty(dismissedVersions) 
                ? new List<string>() 
                : System.Text.Json.JsonSerializer.Deserialize<List<string>>(dismissedVersions) ?? new List<string>();
            
            if (string.IsNullOrEmpty(storedVersion))
            {
                // ó�� �湮 - ���� ���� ����
                var versionToStore = serverVersionInfo?.Version ?? FALLBACK_VERSION;
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "tablecloth-version", versionToStore);
                Console.WriteLine($"ù �湮: ���� {versionToStore} ����");
            }
            else if (serverVersionInfo != null && 
                     storedVersion != serverVersionInfo.Version &&
                     !dismissedVersionsList.Contains(serverVersionInfo.Version))
            {
                // �� ���� �����ϰ�, ����ڰ� ������ "���߿�"�� �������� ���� ��츸 �˸�
                _pendingUpdate = serverVersionInfo;
                await ShowGentleUpdateNotificationAsync();
                Console.WriteLine($"�� ���� ����: {serverVersionInfo.Version} (����: {storedVersion})");
            }
            else if (serverVersionInfo != null && dismissedVersionsList.Contains(serverVersionInfo.Version))
            {
                Console.WriteLine($"���� {serverVersionInfo.Version}�� ����ڰ� ������ ������");
            }
            
            // ��׶��忡�� �ֱ��� ���� üũ ����
            StartPeriodicVersionCheck();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"���� üũ �� ����: {ex.Message}");
        }
    }

    // �������� ���� ���� �������� - ����ȭ�� ����
    private async Task<VersionInfo?> GetServerVersionAsync()
    {
        try
        {
            var cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // JavaScript fetch API ���� ����Ͽ� �� ���� ���� �ڵ鸵
            var fetchResult = await SafeInvokeJSWithResultAsync<string>("fetchVersionJson", $"/version.json?t={cacheBuster}");
            
            if (!string.IsNullOrEmpty(fetchResult))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(fetchResult);
                var root = doc.RootElement;
                
                var versionInfo = new VersionInfo
                {
                    Version = root.TryGetProperty("version", out var version) ? version.GetString() : null,
                    BuildDate = root.TryGetProperty("buildDate", out var buildDate) ? buildDate.GetString() : null,
                    Commit = root.TryGetProperty("commit", out var commit) ? commit.GetString() : null,
                    Branch = root.TryGetProperty("branch", out var branch) ? branch.GetString() : null
                };
                
                Console.WriteLine($"���� ���� ���� �ε� ����: {versionInfo.Version} (����: {versionInfo.BuildDate})");
                return versionInfo;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"���� ���� ���� �������� ����: {ex.Message}");
            // 404�� ��Ʈ��ũ ���� �� ������ ó��
        }

        return null;
    }

    // ����� ��ȯ�ϴ� ������ JavaScript ȣ��
    private async Task<T?> SafeInvokeJSWithResultAsync<T>(string identifier, params object[] args)
    {
        try
        {
            return await JSRuntime.InvokeAsync<T>(identifier, args);
        }
        catch (JSException ex) when (ex.Message.Contains("undefined"))
        {
            Console.WriteLine($"JavaScript �Լ� '{identifier}'�� ���ǵ��� ����: {ex.Message}");
            return default;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JavaScript ȣ�� ���� '{identifier}': {ex.Message}");
            return default;
        }
    }

    // �ε巯�� ������Ʈ �˸� ǥ�� - confirm ���� ����
    private async Task ShowGentleUpdateNotificationAsync()
    {
        if (_pendingUpdate is null) return;

        // ��Ŀ�� ���ѱ� ���� ������ ���� �˸��� ǥ��
        _showUpdateNotification = true;
        StateHasChanged();

        // 5�� �� �ڵ� ���� ó�� (����ڰ� ���� ���� ���� ���)
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(5));
            await InvokeAsync(() =>
            {
                if (_showUpdateNotification)
                {
                    _showUpdateNotification = false;
                    StateHasChanged();
                }
            });
        });
    }

    // ������Ʈ �˸� �ݱ� - ����ڰ� "���߿�" ���� �� ���
    private async Task DismissUpdateNotification()
    {
        if (_pendingUpdate is not null)
        {
            // ���� ������ "���õ� ����" ��Ͽ� �߰�
            var dismissedVersions = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-dismissed-versions");
            var dismissedVersionsList = string.IsNullOrEmpty(dismissedVersions) 
                ? new List<string>() 
                : System.Text.Json.JsonSerializer.Deserialize<List<string>>(dismissedVersions) ?? new List<string>();
            
            if (!dismissedVersionsList.Contains(_pendingUpdate.Version ?? ""))
            {
                dismissedVersionsList.Add(_pendingUpdate.Version ?? "");
                
                // �ִ� 5�� ������ ��� (�ʹ� ���� ������ �ʵ���)
                if (dismissedVersionsList.Count > 5)
                {
                    dismissedVersionsList.RemoveRange(0, dismissedVersionsList.Count - 5);
                }
                
                var dismissedVersionsJson = System.Text.Json.JsonSerializer.Serialize(dismissedVersionsList);
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "tablecloth-dismissed-versions", dismissedVersionsJson);
                
                Console.WriteLine($"���� {_pendingUpdate.Version}�� ���� ��Ͽ� �߰�");
            }
        }
        
        _showUpdateNotification = false;
        StateHasChanged();
    }

    // ������Ʈ ���� - ���� ��� ����
    private async Task ApplyUpdateAsync()
    {
        if (_pendingUpdate is null) return;

        // �� ������ ���� �������� ����
        await JSRuntime.InvokeVoidAsync("localStorage.setItem", "tablecloth-version", _pendingUpdate.Version);
        
        // ���� ��Ͽ��� ���� ������ ���� (�� ���� ���� �� �ʱ�ȭ)
        await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "tablecloth-dismissed-versions");
        
        Console.WriteLine($"���� {_pendingUpdate.Version}�� ������Ʈ ����");
        
        // ����Ʈ ���ΰ�ħ ����
        await SafeInvokeJSAsync("window.forceRefresh");
    }

    // �ֱ��� ���� üũ ���� - �� ����
    private void StartPeriodicVersionCheck()
    {
        // ���� Ÿ�̸Ӱ� �ִٸ� ����
        _updateCheckTimer?.Dispose();

        // 1�ð����� ��׶��忡�� üũ (30�п��� ����)
        _updateCheckTimer = new Timer(async _ =>
        {
            try
            {
                await InvokeAsync(async () =>
                {
                    var serverVersionInfo = await GetServerVersionAsync();
                    var storedVersion = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-version");
                    var dismissedVersions = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "tablecloth-dismissed-versions");
                    var dismissedVersionsList = string.IsNullOrEmpty(dismissedVersions) 
                        ? new List<string>() 
                        : System.Text.Json.JsonSerializer.Deserialize<List<string>>(dismissedVersions) ?? new List<string>();
                    
                    if (serverVersionInfo != null && 
                        storedVersion != serverVersionInfo.Version && 
                        !dismissedVersionsList.Contains(serverVersionInfo.Version) &&
                        !_showUpdateNotification) // �̹� �˸��� ǥ�õ��� ���� ��츸
                    {
                        _pendingUpdate = serverVersionInfo;
                        await ShowGentleUpdateNotificationAsync();
                        Console.WriteLine($"�ֱ��� üũ: �� ���� {serverVersionInfo.Version} ����");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"�ֱ��� ���� üũ �� ����: {ex.Message}");
            }
        });
        
        // 1�ð� �� �����Ͽ� 1�ð� �������� ����
        _updateCheckTimer.Change(TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    private async Task InitializeSidebarState()
    {
        try
        {
            var windowWidth = await SafeInvokeJSWithResultAsync<int>("getWindowWidth");
            
            // ����ũ��(768px �ʰ�)������ �⺻������ ���� ����
            // �����(768px ����)������ �⺻������ ���� ����
            _isSidebarOpen = windowWidth > 768;
            _isInitialized = true;
            
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"���̵�� �ʱ� ���� ���� �� ����: {ex.Message}");
            // ���� �߻� �� ����ũ�� �⺻������ ����
            _isSidebarOpen = true;
            _isInitialized = true;
            StateHasChanged();
        }
    }

    // JavaScript���� ȣ���� �� �ִ� �޼��� (â ũ�� ���� ��)
    [JSInvokable]
    public Task OnWindowResize(int width)
    {
        var isMobile = width <= 768;
        
        // ����Ͽ��� ����ũ������ ��ȯ �� ���̵�� ����
        if (!isMobile && !_isSidebarOpen)
        {
            _isSidebarOpen = true;
            StateHasChanged();
        }
        // ����ũ�鿡�� ����Ϸ� ��ȯ �� ���̵�� �ݱ� (��, �̹� �����ִٸ�)
        else if (isMobile && _isSidebarOpen)
        {
            _isSidebarOpen = false;
            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    private async Task HandleLoginAsync()
    {
        // ���� ���� �÷ο� ����
        await AuthService.StartAuthFlowAsync();
    }

    private async Task OpenSettingDialog()
    {
        _showSettingsModal = true;
        StateHasChanged();
        await Task.CompletedTask;
    }

    private async Task OpenServicesModalAsync()
    {
        _showServicesModal = true;
        StateHasChanged();
        await Task.CompletedTask;
    }

    private void ToggleSidebar()
    {
        _isSidebarOpen = !_isSidebarOpen;
        StateHasChanged();
    }

    // ���� ������Ʈ ���� �޼���
    private async Task SetExamplePrompt(string prompt)
    {
        if (!_hasApiKey)
        {
            await HandleLoginAsync();
            return;
        }

        _userInput = prompt;
        StateHasChanged();
        
        // �ٷ� �޽��� ����
        await SendMessage();
    }

    // �Է� ������ ����� �� ȣ��Ǵ� �޼���
    private async Task OnInputChange(ChangeEventArgs e)
    {
        var newValue = e.Value?.ToString() ?? string.Empty;

        // �ִ� ���̸� �ʰ��ϴ� ��� �߶󳻱�
        if (newValue.Length > _maxInputLength)
            newValue = newValue.Substring(0, _maxInputLength);

        _userInput = newValue;
        
        // �ؽ�Ʈ ���� �ڵ� ��������
        await SafeInvokeJSAsync("autoResizeTextarea", "chatTextArea");
    }

    private async Task SendMessage()
    {
        if (!_hasApiKey)
        {
            await HandleLoginAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(_userInput) || _isStreaming)
            return;

        // ����Ͽ��� �޽��� ���� �� ���̵�� �ݱ�
        var windowWidth = await SafeInvokeJSWithResultAsync<int>("getWindowWidth");
        if (windowWidth <= 768 && _isSidebarOpen)
        {
            _isSidebarOpen = false;
        }

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

            await SafeInvokeJSAsync("scrollToBottom", "messages");

            await foreach (var chunk in ChatService.SendMessageStreamingAsync(_client, input, _sessionId))
            {
                _currentStreamedMessage += chunk;
                StateHasChanged();
                await Task.Delay(10); // �ڿ������� Ÿ���� ȿ��
            }

            _messages.Add(new ChatMessage { Content = _currentStreamedMessage, IsUser = false });
        }
        catch (Exception ex)
        {
            _messages.Add(new ChatMessage
            {
                Content = $"�˼��մϴ�. ������ �߻��߽��ϴ�: {ex.Message}",
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
        // ������� ���� ��ȭ ������ �ִٸ� �ε巯�� Ȯ�� ó��
        if (_hasUnsavedContent)
        {
            // confirm ��� �ξ� Ȯ�� ���̾�α� ��� ����
            var shouldLogout = await JSRuntime.InvokeAsync<bool>("confirm", 
                "���� ���� ���� ��ȭ ������ �ֽ��ϴ�. �α׾ƿ��ϸ� ��ȭ ������ ������ϴ�. ���� �α׾ƿ��Ͻðڽ��ϱ�?");
            
            if (!shouldLogout)
            {
                return;
            }
        }

        await JSRuntime.InvokeAsync<string>("localStorage.setItem", "openRouterApiKey", string.Empty);
        
        // ���� ������Ʈ
        _hasApiKey = false;
        _client = null;
        _messages.Clear();
        _userInput = string.Empty;
        _isStreaming = false;
        _currentStreamedMessage = string.Empty;
        _sessionId = Guid.NewGuid().ToString();
        
        StateHasChanged();
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && e.ShiftKey == false)
        {
            await SendMessage();
        }
    }

    private MarkupString FormatMessage(string markdown)
    {
        var html = string.Empty;

        if (string.IsNullOrWhiteSpace(markdown))
            return (MarkupString)html;

        // ��ũ�ٿ��� HTML�� ��ȯ
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
            eachAnchorElem.SetAttribute("style", "font-weight: bold; cursor: pointer; color: #2563eb; text-decoration: underline;");
            eachAnchorElem.InnerHtml = WebUtility.HtmlEncode(currentHref);
        }

        html = document.Body.InnerHtml;

        return (MarkupString)html;
    }

    private async Task ResetConversationAsync()
    {
        // ������� ���� ��ȭ ������ �ִٸ� �ε巯�� Ȯ�� ó��
        if (_hasUnsavedContent)
        {
            // confirm ��� �ξ� Ȯ�� ���̾�α� ��� ����
            var shouldReset = await JSRuntime.InvokeAsync<bool>("confirm", 
                "���� ���� ���� ��ȭ ������ �ֽ��ϴ�. ���ο� ä���� �����ϸ� ���� ��ȭ ������ ������ϴ�. ����Ͻðڽ��ϱ�?");
            
            if (!shouldReset)
            {
                return;
            }
        }

        _messages.Clear();
        _sessionId = Guid.NewGuid().ToString();
        await ChatService.ClearSessionAsync(_sessionId);
        
        // ����Ͽ��� ��ȭ ���� �� ���̵�� �ݱ�
        var windowWidth = await SafeInvokeJSWithResultAsync<int>("getWindowWidth");
        if (windowWidth <= 768 && _isSidebarOpen)
        {
            _isSidebarOpen = false;
        }
        
        StateHasChanged();
    }

    // ��ȭ �׼� ��Ӵٿ� ���
    private void ToggleConversationActionsDropdown()
    {
        _showConversationActionsDropdown = !_showConversationActionsDropdown;
        StateHasChanged();
    }

    // ���downs���� �μ� �� �����
    private async Task PrintAndHideDropdown()
    {
        await PrintConversationAsync();
        _showConversationActionsDropdown = false;
        StateHasChanged();
    }

    // ���downs���� �������� �� �����
    private async Task ExportAndHideDropdown()
    {
        await ExportConversationAsTextAsync();
        _showConversationActionsDropdown = false;
        StateHasChanged();
    }

    // ���down���� ���� �� �����
    private async Task ShareAndHideDropdown()
    {
        await ShareConversationAsync();
        _showConversationActionsDropdown = false;
        StateHasChanged();
    }

    // ��ȭ ���� �μ� �޼��� - confirm�� �� �� �ε巯�� �˸����� ����
    private async Task PrintConversationAsync()
    {
        if (!_messages.Any())
        {
            // confirm ��� �ܼ� alert ����ϰų� �佺Ʈ �˸����� ���� ����
            await SafeInvokeJSAsync("showToast", "�μ��� ��ȭ ������ �����ϴ�.", "info");
            return;
        }

        // ����ڿ��� �μ� ��� ���� �ɼ� ���� - �� �ε巯�� ������� ���� �ʿ��
        // ����� �⺻������ �̸����� â ���
        var printHtml = GeneratePrintHtml();
        await SafeInvokeJSAsync("showPrintPreview", printHtml);
    }

    // �μ�� HTML ����
    private string GeneratePrintHtml()
    {
        var html = new System.Text.StringBuilder();
        
        // HTML ���
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang='ko'>");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset='UTF-8'>");
        html.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        html.AppendLine("<title>TableClothLite AI ��ȭ ���</title>");
        html.AppendLine("<style>");
        html.AppendLine(GetPrintStyles());
        html.AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        
        // ��� ����
        html.AppendLine("<div class='print-header'>");
        html.AppendLine("<h1>TableClothLite AI ��ȭ ���</h1>");
        html.AppendLine($"<p class='print-date'>������: {DateTime.Now:yyyy�� MM�� dd�� HH:mm}</p>");
        html.AppendLine($"<p class='print-info'>�� {_messages.Count}���� �޽���</p>");
        html.AppendLine("</div>");
        
        // ��ȭ ����
        html.AppendLine("<div class='conversation'>");
        
        for (int i = 0; i < _messages.Count; i++)
        {
            var message = _messages[i];
            var messageClass = message.IsUser ? "user-message" : "assistant-message";
            var sender = message.IsUser ? "�����" : "TableClothLite AI";
            
            html.AppendLine($"<div class='message {messageClass}'>");
            html.AppendLine($"<div class='message-header'>");
            html.AppendLine($"<span class='sender'>{sender}</span>");
            html.AppendLine($"<span class='message-number'>#{i + 1}</span>");
            html.AppendLine("</div>");
            html.AppendLine($"<div class='message-content'>");
            
            // ��ũ�ٿ��� HTML�� ��ȯ�ϵ� �μ������ ����
            var content = ConvertMarkdownForPrint(message.Content);
            html.AppendLine(content);
            
            html.AppendLine("</div>");
            html.AppendLine("</div>");
        }
        
        html.AppendLine("</div>");
        
        // Ǫ��
        html.AppendLine("<div class='print-footer'>");
        html.AppendLine("<p>TableClothLite AI - ������ ���� �ι� AI ��ý���Ʈ</p>");
        html.AppendLine("<p>https://yourtablecloth.app</p>");
        html.AppendLine("</div>");
        
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        
        return html.ToString();
    }

    // �μ�� CSS ��Ÿ��
    private string GetPrintStyles()
    {
        return @"
            @media screen {
                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                    line-height: 1.6;
                    color: #333;
                    max-width: 800px;
                    margin: 0 auto;
                    padding: 20px;
                    background: #f9f9f9;
                }
            }
            
            @media print {
                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                    line-height: 1.5;
                    color: #000;
                    margin: 0;
                    padding: 15mm;
                    background: white;
                }
                
                .print-header {
                    border-bottom: 2px solid #333;
                    padding-bottom: 10px;
                    margin-bottom: 20px;
                }
                
                .message {
                    page-break-inside: avoid;
                    break-inside: avoid;
                }
                
                .print-footer {
                    position: fixed;
                    bottom: 10mm;
                    left: 15mm;
                    right: 15mm;
                    border-top: 1px solid #ccc;
                    padding-top: 5px;
                    font-size: 0.8em;
                    text-align: center;
                    color: #666;
                }
            }
            
            .print-header h1 {
                margin: 0 0 10px 0;
                font-size: 24px;
                color: #2563eb;
            }
            
            .print-date, .print-info {
                margin: 5px 0;
                color: #666;
                font-size: 14px;
            }
            
            .conversation {
                margin: 20px 0;
            }
            
            .message {
                margin-bottom: 20px;
                border: 1px solid #e5e7eb;
                border-radius: 8px;
                overflow: hidden;
            }
            
            .message-header {
                background: #f3f4f6;
                padding: 8px 12px;
                display: flex;
                justify-content: space-between;
                align-items: center;
                font-size: 12px;
                color: #6b7280;
            }
            
            .user-message .message-header {
                background: #eff6ff;
            }
            
            .assistant-message .message-header {
                background: #f9fafb;
            }
            
            .sender {
                font-weight: 600;
                color: #374151;
            }
            
            .user-message .sender {
                color: #2563eb;
            }
            
            .assistant-message .sender {
                color: #059669;
            }
            
            .message-number {
                font-size: 11px;
                color: #9ca3af;
            }
            
            .message-content {
                padding: 12px;
                background: white;
            }
            
            .message-content h1, .message-content h2, .message-content h3,
            .message-content h4, .message-content h5, .message-content h6 {
                margin-top: 0;
                margin-bottom: 10px;
                color: #374151;
            }
            
            .message-content p {
                margin: 0 0 10px 0;
            }
            
            .message-content ul, .message-content ol {
                margin: 0 0 10px 20px;
                padding-left: 0;
            }
            
            .message-content li {
                margin-bottom: 5px;
            }
            
            .message-content pre {
                background: #f3f4f6;
                padding: 10px;
                border-radius: 4px;
                overflow-x: auto;
                font-size: 12px;
                margin: 10px 0;
            }
            
            .message-content code {
                background: #f3f4f6;
                padding: 2px 4px;
                border-radius: 3px;
                font-size: 13px;
            }
            
            .message-content blockquote {
                border-left: 3px solid #d1d5db;
                margin: 10px 0;
                padding: 5px 0 5px 15px;
                color: #6b7280;
                font-style: italic;
            }
            
            .message-content table {
                border-collapse: collapse;
                width: 100%;
                margin: 10px 0;
                font-size: 13px;
            }
            
            .message-content th, .message-content td {
                border: 1px solid #d1d5db;
                padding: 6px 8px;
                text-align: left;
            }
            
            .message-content th {
                background: #f9fafb;
                font-weight: 600;
            }
            
            .print-footer p {
                margin: 2px 0;
            }
            
            @page {
                margin: 15mm;
                size: A4;
            }
        ";
    }

    // ��ũ�ٿ��� �μ�� HTML�� ��ȯ
    private string ConvertMarkdownForPrint(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        try
        {
            // ��ũ�ٿ��� HTML�� ��ȯ
            var html = Markdown.ToHtml(markdown, _markdownPipeline);
            var document = _htmlParser.ParseDocument(html);

            if (document.Body == null)
                return WebUtility.HtmlEncode(markdown);

            // �μ������ ��ũ ó�� (href �����ϰ� �ؽ�Ʈ�� ǥ��)
            foreach (var anchor in document.QuerySelectorAll("a"))
            {
                var href = anchor.GetAttribute("href") ?? string.Empty;
                if (!string.IsNullOrEmpty(href))
                {
                    anchor.RemoveAttribute("href");
                    anchor.RemoveAttribute("onclick");
                    anchor.SetAttribute("style", "color: #2563eb; text-decoration: underline;");
                    
                    // ��ũ URL�� �ؽ�Ʈ �ڿ� ��ȣ�� �߰�
                    if (anchor.TextContent != href)
                    {
                        anchor.InnerHtml = $"{anchor.InnerHtml} ({WebUtility.HtmlEncode(href)})";
                    }
                }
            }

            // �̹��� ó�� (alt �ؽ�Ʈ�� ��ü)
            foreach (var img in document.QuerySelectorAll("img"))
            {
                var alt = img.GetAttribute("alt") ?? "�̹���";
                var src = img.GetAttribute("src") ?? "";
                
                var replacement = document.CreateElement("span");
                replacement.SetAttribute("style", "background: #f3f4f6; padding: 4px 8px; border-radius: 4px; font-style: italic;");
                replacement.TextContent = $"[�̹���: {alt}]";
                
                img.ParentElement?.ReplaceChild(replacement, img);
            }

            return document.Body.InnerHtml;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"��ũ�ٿ� ��ȯ �� ����: {ex.Message}");
            return WebUtility.HtmlEncode(markdown);
        }
    }

    // ��ȭ ������ �ؽ�Ʈ ���Ϸ� �������� - alert�� �佺Ʈ�� ���� ����
    private async Task ExportConversationAsTextAsync()
    {
        if (!_messages.Any())
        {
            // confirm ��� �ε巯�� �˸�
            await SafeInvokeJSAsync("showToast", "������ ��ȭ ������ �����ϴ�.", "info");
            return;
        }
        
        var conversationData = new
        {
            exportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            messages = _messages.Select(m => new
            {
                content = m.Content,
                isUser = m.IsUser
            }).ToArray()
        };
        
        var jsonData = System.Text.Json.JsonSerializer.Serialize(conversationData);
        var success = await SafeInvokeJSWithResultAsync<bool>("exportConversationAsText", jsonData);
        
        if (!success)
        {
            await SafeInvokeJSAsync("showToast", "�ؽ�Ʈ ���� �������⿡ �����߽��ϴ�.", "error");
        }
    }

    // ��ȭ ���� ���� - alert�� �佺Ʈ�� ����
    private async Task ShareConversationAsync()
    {
        if (!_messages.Any())
        {
            await SafeInvokeJSAsync("showToast", "������ ��ȭ ������ �����ϴ�.", "info");
            return;
        }

        try
        {
            var shareText = GenerateShareText();
            
            var shareData = new
            {
                title = "TableClothLite AI ��ȭ ���",
                text = shareText
            };
            
            // JavaScript�� shareContent �Լ� ȣ��
            var result = await SafeInvokeJSWithResultAsync<ShareResult>("shareContent", shareData);
            
            if (result?.Success == true)
            {
                switch (result.Method)
                {
                    case "webshare":
                        // Web Share API�� ���������� ������ - ���� �˸� ���ʿ�
                        break;
                    case "clipboard":
                        await SafeInvokeJSAsync("showToast", 
                            "��ȭ ������ Ŭ�����忡 ����Ǿ����ϴ�. �ٸ� �ۿ��� �ٿ��ֱ��Ͽ� ������ �� �ֽ��ϴ�.", 
                            "success");
                        break;
                }
            }
            else
            {
                // ��� ��� ����
                await SafeInvokeJSAsync("showToast", 
                    "���� ����� ����� �� �����ϴ�. ��� �ؽ�Ʈ ���Ϸ� �������⸦ ����غ�����.", 
                    "warning");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"��ȭ ���� �� ����: {ex.Message}");
            await SafeInvokeJSAsync("showToast", "��ȭ ������ �����߽��ϴ�.", "error");
        }
    }

    // ������ �ؽ�Ʈ ����
    private string GenerateShareText()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("TableClothLite AI ��ȭ ���");
        text.AppendLine($"������: {DateTime.Now:yyyy�� MM�� dd�� HH:mm}");
        text.AppendLine(new string('=', 40));
        text.AppendLine();
        
        for (int i = 0; i < _messages.Count; i++)
        {
            var message = _messages[i];
            var sender = message.IsUser ? "�����" : "AI";
            
            text.AppendLine($"[{i + 1}] {sender}:");
            text.AppendLine(message.Content.Trim());
            text.AppendLine();
        }
        
        text.AppendLine(new string('=', 40));
        text.AppendLine("TableClothLite AI - https://yourtablecloth.app");
        
        return text.ToString();
    }

    // ��� �ݱ� �޼���
    private void CloseSandboxGuide()
    {
        _showSandboxGuide = false;
        StateHasChanged();
    }

    // ���� ��� ��� �ݱ�
    private void CloseServicesModal()
    {
        _showServicesModal = false;
        StateHasChanged();
    }

    // ���� ��� �ݱ� - �� �ε������� ���ΰ�ħ �߰�
    private async Task CloseSettingsModal()
    {
        _showSettingsModal = false;
        
        // �� ������ ����Ǿ��� �� �����Ƿ� ModelIndicator ���ΰ�ħ
        if (_modelIndicator is not null)
        {
            await _modelIndicator.RefreshConfig();
        }
        
        StateHasChanged();
    }

    // �Ŀ� ��� ����
    private async Task DismissSponsorBanner()
    {
        _sponsorBannerDismissed = true;
        StateHasChanged();
        
        // ���� ���丮���� �����Ͽ� ���� �湮�ÿ��� ���
        try
        {
            await JSRuntime.InvokeVoidAsync("localStorage.setItem", "sponsor-banner-dismissed", "true");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"�Ŀ� ��� ���� ���� �� ����: {ex.Message}");
        }
    }

    public void Dispose()
    {
        // beforeunload �ڵ鷯 ����
        try
        {
            JSRuntime.InvokeVoidAsync("cleanupBeforeUnloadHandler");
        }
        catch
        {
            // Disposal �� ������ ����
        }

        // Ÿ�̸� ����
        _updateCheckTimer?.Dispose();

        dotNetHelper?.Dispose();
    }
}