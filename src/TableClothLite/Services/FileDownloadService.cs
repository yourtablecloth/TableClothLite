using Microsoft.JSInterop;

namespace TableClothLite.Services;

public sealed class FileDownloadService
{
    public FileDownloadService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    private readonly IJSRuntime _jsRuntime;

    public async Task DownloadFileAsync(
        Stream readableStream, string fileName,
        string contentType = "application/octet-stream",
        string jsCalleeIdentifier = "downloadFileStream",
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        if (readableStream == null)
            throw new ArgumentNullException(nameof(readableStream));
        if (!readableStream.CanRead)
            throw new ArgumentException("The stream must be readable.", nameof(readableStream));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("The file name must not be null or empty.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(jsCalleeIdentifier))
            throw new ArgumentException("The JS function identifier must not be null or empty.", nameof(jsCalleeIdentifier));

        using var streamReference = new DotNetStreamReference(readableStream, leaveOpen);
        await _jsRuntime.InvokeVoidAsync(jsCalleeIdentifier, fileName, contentType, streamReference);
    }
}
