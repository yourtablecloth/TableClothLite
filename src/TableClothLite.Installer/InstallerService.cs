using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using TableClothLite.Shared.Models;
using TableClothLite.Shared.Services;

public sealed class InstallerService : BackgroundService
{
    private readonly ILogger<InstallerService> _logger;
    private readonly CatalogService _catalogService;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public InstallerService(
        ILogger<InstallerService> logger,
        CatalogService catalogService,
        IHostApplicationLifetime lifetime,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _catalogService = catalogService;
        _lifetime = lifetime;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    private List<ServiceInfo> Services { get; } = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("InstallerService is running.");
            await _catalogService.LoadCatalogDocumentAsync(Services, stoppingToken).ConfigureAwait(false);

            var targetServicesIds = (_configuration["Services"] ?? string.Empty)
                .Split([',',], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim());

            var targetUrl = (_configuration["Url"] ?? string.Empty).Trim();

            var client = _httpClientFactory.CreateClient();
            var foundServices = new List<ServiceInfo>();
            var installTaskList = new List<ProcessStartInfo>();

            foreach (var eachServiceId in targetServicesIds)
            {
                var foundService = Services.FirstOrDefault(x => string.Equals(eachServiceId, x.ServiceId, StringComparison.OrdinalIgnoreCase));

                if (foundService == null)
                {
                    _logger.LogWarning("Service '{ServiceId}' not found in catalog.", eachServiceId);
                    continue;
                }

                foundServices.Add(foundService);

                foreach (var eachPackage in foundService.Packages)
                {
                    _logger.LogInformation("Downloading package '{PackageName}' for service '{ServiceId}'.", eachPackage.PackageName, foundService.ServiceId);

                    var uniqueId = $"{foundService.ServiceId}_{eachPackage.PackageName}";
                    await using var stream = await client.GetStreamAsync(eachPackage.PackageUrl, stoppingToken).ConfigureAwait(false);

                    var tempPath = Path.Combine(Path.GetTempPath(), $"{uniqueId}.exe");
                    await using var fileStream = File.Open(tempPath, FileMode.Create, FileAccess.Write);
                    await stream.CopyToAsync(fileStream, stoppingToken).ConfigureAwait(false);

                    installTaskList.Add(new ProcessStartInfo(tempPath, eachPackage.Arguments)
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    });
                }
            }

            foreach (var eachInstallTask in installTaskList)
            {
                _logger.LogInformation(
                    "Launching '{installer}' installer...",
                    Path.GetFileName(eachInstallTask.FileName));

                using var process = new Process()
                {
                    StartInfo = eachInstallTask,
                    EnableRaisingEvents = true,
                };

                if (!process.Start())
                {
                    _logger.LogError("Failed to start installer process.");
                    continue;
                }

                await process.WaitForExitAsync(stoppingToken).ConfigureAwait(false);

                _logger.Log((process.ExitCode != 0 ? LogLevel.Warning : LogLevel.Information),
                    "Installer process '{installer}' exited with code {ExitCode}.",
                    Path.GetFileName(eachInstallTask.FileName), process.ExitCode);
            }

            var stsessFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "AhnLab", "Safe Transaction", "StSess.exe");

            if (File.Exists(stsessFilePath))
            {
                await TryShowMessageAsync(
                    "AhnLab Safe Transaction이 설치되어있습니다. 원격 접속 차단 설정을 꺼야 샌드박스 창이 닫히지 않습니다.",
                    stoppingToken).ConfigureAwait(false);

                _logger.LogInformation("Launching 'StSess.exe' with '/config' option...");
                using var stsessProcess = Process.Start(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                    $"/c \"{stsessFilePath}\" /config");
                await stsessProcess.WaitForExitAsync(stoppingToken).ConfigureAwait(false);

                await TryShowMessageAsync(
                    "설정을 완료했다면 확인 버튼을 눌러주세요.",
                    stoppingToken).ConfigureAwait(false);
            }

            var urls = new List<string>();
            urls.AddRange(foundServices.Select(x => x.Url));

            if (!string.IsNullOrWhiteSpace(targetUrl))
                urls.Add(targetUrl);

            foreach (var eachUrl in urls)
            {
                _logger.LogInformation("Opening web site '{url}'...", Path.GetFileName(eachUrl));

                var startInfo = new ProcessStartInfo(eachUrl)
                {
                    UseShellExecute = true,
                };

                Process.Start(startInfo);
            }
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            _logger.LogError(ex, "Exception occurred while running installer.");
        }
        finally
        {
            _logger.LogInformation("Exit code: {exitCode}", Environment.ExitCode);
            _lifetime.StopApplication();
        }
    }

    private async Task<bool> TryShowMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        var msgPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "msg.exe");

        if (!File.Exists(msgPath))
            return false;

        _logger.LogInformation("Launching 'msg.exe' for user notification...");
        using var msgProcess = Process.Start(msgPath, $"{Environment.UserName} /w \"{message}\"");
        await msgProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
