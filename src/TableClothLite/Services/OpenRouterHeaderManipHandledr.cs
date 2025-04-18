namespace TableClothLite.Services;

public sealed class OpenRouterHeaderManipHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var openaiBetaHeader = "OpenAI-Beta";

        if (request.Headers.Contains(openaiBetaHeader))
            request.Headers.Remove(openaiBetaHeader);

        return base.SendAsync(request, cancellationToken);
    }
}
