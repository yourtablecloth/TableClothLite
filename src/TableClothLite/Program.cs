using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using TableClothLite;
using TableClothLite.Services;
using TableClothLite.ViewModels;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddFluentUIComponents(options =>
{
    options.ValidateClassNames = false;
});

builder.Services.AddFluentUIComponents();

builder.Services.AddSingleton<SandboxComposerService>();

builder.Services.AddScoped<FileDownloadService>();

builder.Services.AddScoped<SandboxViewModel>();

builder.Services.AddScoped(sp =>
{
    return new HttpClient
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    };
});

await using var app = builder.Build();
await app.RunAsync();
