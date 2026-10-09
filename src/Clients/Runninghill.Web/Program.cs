using Runninghill.Diagnostics;
using Runninghill.Clients;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Runninghill.Web.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var recentLogs = new RecentLogStore();
builder.Services.AddSingleton(recentLogs);
builder.Logging.AddProvider(recentLogs);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
// Reuse one client. Requests stay on this web origin; nginx forwards /api to the service.
var serviceAddress = new Uri(builder.HostEnvironment.BaseAddress);
#if DEBUG
// The standalone debug server serves UI assets only. Its API lives on a separate local port.
serviceAddress = new Uri(builder.Configuration["ServiceUrl"] ?? builder.HostEnvironment.BaseAddress);
#endif
builder.Services.AddSingleton(new HttpClient
{
    BaseAddress = serviceAddress, Timeout = ClientMessages.RequestTimeout,
    // Lists are paged. Bound buffering so a bad server cannot fill client memory.
    MaxResponseContentBufferSize = 256 * 1024
});
var host = builder.Build();
var javascript = host.Services.GetRequiredService<Microsoft.JSInterop.IJSRuntime>();
Runninghill.Contracts.AppText.SetClientLanguage(await Microsoft.JSInterop.JSRuntimeExtensions.InvokeAsync<string>(javascript, "runninghillLanguage.get"));
await host.RunAsync();
