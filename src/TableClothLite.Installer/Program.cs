using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using TableClothLite.Shared.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddCommandLine(args);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    { "TableClothCatalogBaseUrl", "https://yourtablecloth.app/TableClothCatalog/" },
});

builder.Logging.ClearProviders();

bool diagnoseLogging = string.Equals(bool.TrueString, builder.Configuration["DiagnoseLogging"], StringComparison.OrdinalIgnoreCase);
if (!diagnoseLogging)
{
    builder.Logging.Services.AddSingleton<ConsoleFormatter, SimpleConsoleFormatter>();
    builder.Logging.AddConsole(o => o.FormatterName = nameof(SimpleConsoleFormatter));
}
else
{
    builder.Logging.AddConsole();
}

builder.Services.AddHttpClient().AddResilienceEnricher();
builder.Services.AddSingleton<CatalogService>();

builder.Services.AddHostedService<InstallerService>();

var app = builder.Build();

app.Run();
