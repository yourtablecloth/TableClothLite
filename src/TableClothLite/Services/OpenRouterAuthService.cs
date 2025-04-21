using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TableClothLite.Models;

namespace TableClothLite.Services;

public sealed class OpenRouterAuthService
{
    private readonly HttpClient _httpClient;
    private readonly NavigationManager _navigationManager;
    private readonly IJSRuntime _jsRuntime;

    public OpenRouterAuthService(HttpClient httpClient, NavigationManager navigationManager, IJSRuntime jsRuntime)
    {
        _httpClient = httpClient;
        _navigationManager = navigationManager;
        _jsRuntime = jsRuntime;
    }

    public PkceChallenge GeneratePkce()
    {
        // Generate a random code verifier
        byte[] buffer = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(buffer);

        string codeVerifier = Base64UrlEncode(buffer);

        // Generate code challenge from code verifier
        using var sha256 = SHA256.Create();
        byte[] challengeBytes = sha256.ComputeHash(new UTF8Encoding(false).GetBytes(codeVerifier));
        string codeChallenge = Base64UrlEncode(challengeBytes);

        return new PkceChallenge(codeChallenge, codeVerifier);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public async Task StartAuthFlowAsync(CancellationToken cancellationToken = default)
    {
        var challenge = GeneratePkce();
        var callbackUrl = _navigationManager.BaseUri + "auth-callback";
        var authUrl = string.Join(
            "?",
            "https://openrouter.ai/auth",
            string.Join("&", (new Dictionary<string, string>
            {
                { "callback_url", callbackUrl },
                { "code_challenge", challenge.CodeChallenge },
                { "code_challenge_method", "S256" }
            }).Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"))
        );

        // Save the code verifier to session storage for later use
        await _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", cancellationToken, "codeVerifier", challenge.CodeVerifier);

        _navigationManager.NavigateTo(authUrl);
    }

    public async Task<string> ObtainApiKeyAsync(string code)
    {
        // Retrieve code verifier from session storage
        var codeVerifier = await _jsRuntime.InvokeAsync<string>("sessionStorage.getItem", "codeVerifier");

        if (string.IsNullOrEmpty(codeVerifier))
            throw new InvalidOperationException("Code verifier not found in session storage");

        var requestBody = new
        {
            code,
            code_verifier = codeVerifier,
            code_challenge_method = "S256",
        };

        var response = await _httpClient.PostAsJsonAsync(
            "https://openrouter.ai/api/v1/auth/keys", requestBody);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>();
        return responseJson.GetProperty("key").GetString() ?? throw new InvalidOperationException("API key not found in response");
    }
}
