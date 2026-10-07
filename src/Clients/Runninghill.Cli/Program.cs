using System.Net.Http.Headers;
using System.Net.Http.Json;
using Runninghill.Contracts;

if (args.Length > 1 || (args.Length == 1 && args[0] != "status"))
{
    Console.Error.WriteLine("Usage: Runninghill.Cli [status]. Configure RUNNINGHILL_SERVICE_URL and RUNNINGHILL_ACCESS_TOKEN.");
    return 2;
}
var serviceUrl = Environment.GetEnvironmentVariable("RUNNINGHILL_SERVICE_URL");
var token = Environment.GetEnvironmentVariable("RUNNINGHILL_ACCESS_TOKEN");
if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var address) ||
    (address.Scheme != "https" && !(address.Scheme == "http" && address.IsLoopback)) || string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Set RUNNINGHILL_SERVICE_URL (HTTPS, or HTTP loopback for development) and RUNNINGHILL_ACCESS_TOKEN.");
    return 2;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
try
{
    using var request = new HttpRequestMessage(HttpMethod.Get, "api/status");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var response = await client.SendAsync(request, cancellation.Token);
    response.EnsureSuccessStatusCode();
    var status = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.StatusResponse, cancellation.Token)
        ?? throw new HttpRequestException("The service returned an empty response.");
    Console.WriteLine(status.Message);
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Request cancelled or timed out.");
    return 130;
}
catch (HttpRequestException exception)
{
    Console.Error.WriteLine($"Service request failed: {exception.Message}");
    return 1;
}
