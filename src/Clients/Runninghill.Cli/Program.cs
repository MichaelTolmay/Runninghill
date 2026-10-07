using System.Net.Http.Headers;
using System.Net.Http.Json;
using Runninghill.Contracts;
using Runninghill.Clients;
using System.Text.Json;

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
// Ctrl+C stops the request too, so the server does not keep working after we leave.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 64 * 1024 };
string? requestId = null;
try
{
    using var request = new HttpRequestMessage(HttpMethod.Get, "api/status");
    // Attach the token to this request only; never print it in an error message.
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var response = await client.SendAsync(request, cancellation.Token);
    requestId = ClientMessages.ReadReference(response);
    // An HTTP error is an expected outcome, so do not allocate an exception just to show it.
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine(ClientMessages.WithReference(ClientMessages.ForStatus(response.StatusCode), requestId));
        return 1;
    }
    var status = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.StatusResponse, cancellation.Token)
        ?? throw new JsonException("Missing status response.");
    if (string.IsNullOrWhiteSpace(status.Message))
        throw new JsonException("Missing status message.");
    Console.WriteLine(status.Message);
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine(cancellation.IsCancellationRequested ? "Request cancelled." : ClientMessages.TimedOut);
    // 130 means the user stopped the command; 1 means the request failed.
    return cancellation.IsCancellationRequested ? 130 : 1;
}
catch (HttpRequestException exception)
{
    Console.Error.WriteLine(ClientMessages.WithReference(ClientMessages.ForRequestFailure(exception), requestId));
    return 1;
}
catch (JsonException)
{
    Console.Error.WriteLine(ClientMessages.WithReference(ClientMessages.InvalidReply, requestId));
    return 1;
}
catch (FormatException)
{
    Console.Error.WriteLine("The access token contains invalid characters. Copy a new token and try again.");
    return 2;
}
catch (Exception exception)
{
    // The command boundary should fail cleanly. An error type helps support diagnose a bug
    // without revealing exception text that might contain a token, URL, or server details.
    Console.Error.WriteLine(ClientMessages.WithReference(ClientMessages.Unexpected, requestId));
    Console.Error.WriteLine($"Error type: {exception.GetType().Name}");
    return 1;
}
