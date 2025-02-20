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

            // TODO: ASTX Configuration

            foreach (var eachService in foundServices)
            {
                _logger.LogInformation("Opening web site '{url}'...", Path.GetFileName(eachService.Url));

                var startInfo = new ProcessStartInfo(eachService.Url)
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
}
