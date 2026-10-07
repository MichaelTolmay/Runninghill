using Runninghill.Sdk;
using Runninghill.Service;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRunninghillSdk(builder.Configuration.GetConnectionString("Runninghill")
    ?? "Data Source=:memory:");
builder.Services.AddGrpc();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
app.MapGet("/api/status", async (IRunninghillSdk sdk, CancellationToken cancellationToken) =>
    Results.Json(await sdk.GetStatusAsync(cancellationToken)));
app.MapGrpcService<ApplicationGrpcService>();
app.Run();

public partial class Program;
