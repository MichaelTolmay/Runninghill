using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Runninghill.Web.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
// Reuse one client. Requests stay on this web origin; nginx forwards /api to the service.
builder.Services.AddSingleton(new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress), Timeout = TimeSpan.FromSeconds(10),
    // Status replies are tiny. Bound buffering so a bad server cannot fill client memory.
    MaxResponseContentBufferSize = 64 * 1024
});
await builder.Build().RunAsync();
