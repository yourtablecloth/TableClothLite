using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TableClothLite;
using TableClothLite.Services;
using TableClothLite.Shared.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    { "TableClothCatalogBaseUrl", "https://yourtablecloth.app/TableClothCatalog/" },
});

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddHttpClient();
builder.Services.AddBlazoredLocalStorageAsSingleton();

builder.Services.AddSingleton<SandboxComposerService>();
builder.Services.AddSingleton<CatalogService>();
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<ThemeService>();
builder.Services.AddSingleton<SettingsService>();

builder.Services.AddScoped<FileDownloadService>();
builder.Services.AddScoped<SandboxService>();
builder.Services.AddScoped<WebMcpInteropService>();

builder.Services.AddScoped(sp =>
{
    return new HttpClient
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    };
});

await using var app = builder.Build();
await app.RunAsync();
