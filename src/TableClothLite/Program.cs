using Blazored.LocalStorage;
using KristofferStrube.Blazor.FileSystemAccess;
using Markdig;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TableClothLite;
using TableClothLite.Services;
using TableClothLite.Shared.Services;
using TG.Blazor.IndexedDB;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    { "TableClothCatalogBaseUrl", "https://yourtablecloth.app/TableClothCatalog/" },
});

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");


builder.Services.AddHttpClient();
builder.Services.AddBlazoredLocalStorageAsSingleton();
builder.Services.AddFileSystemAccessService();

builder.Services.AddSingleton<SandboxComposerService>();
builder.Services.AddSingleton<CatalogService>();
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<ThemeService>(); // 테마 서비스 추가

builder.Services.AddScoped<OpenRouterAuthService>();
builder.Services.AddScoped<OpenAIChatService>();
builder.Services.AddScoped<FileDownloadService>();

builder.Services.AddScoped<SandboxService>();

builder.Services.AddScoped(sp =>
{
    return new HttpClient
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    };
});

builder.Services.AddHttpClient("OpenRouter")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
    .AddHttpMessageHandler(() => new OpenRouterHeaderManipHandler())
    .AddDefaultLogger();

builder.Services.AddIndexedDB(dbStore =>
{
    dbStore.DbName = "TableClothLite";
    dbStore.Version = 1;

    dbStore.Stores.Add(new StoreSchema
    {
        Name = "certificates",
        PrimaryKey = new IndexSpec
        {
            Name = "path",
            KeyPath = "path",
            Auto = true,
        },
    });
});

builder.Services.AddScoped(sp =>
{
    return new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseBootstrap()
        .DisableHtml()
        .Build();
});

await using var app = builder.Build();
await app.RunAsync();
