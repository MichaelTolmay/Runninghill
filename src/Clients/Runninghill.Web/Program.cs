using Runninghill.Sdk;
using Runninghill.Web.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRunninghillSdk(builder.Configuration.GetConnectionString("Runninghill")
    ?? "Data Source=:memory:");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
