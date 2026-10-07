using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Runninghill.Clients;
using Runninghill.Contracts;

namespace Runninghill.Maui;

public partial class MainPage : ContentPage
{
    private readonly ILogger<MainPage> logger;
    private HttpClient? client;
    private CancellationTokenSource? activeRequest;

    public MainPage(ILogger<MainPage> logger)
    {
        InitializeComponent();
        this.logger = logger;
    }

    private async void OnCheckClicked(object? sender, EventArgs e)
    {
        // The button is disabled during a request. This guard also stops queued double taps.
        if (activeRequest is not null)
            return;
        if (!Uri.TryCreate(ServiceUrl.Text, UriKind.Absolute, out var address) || address.Scheme != "https" ||
            string.IsNullOrWhiteSpace(AccessToken.Text))
        {
            StatusLabel.Text = "Enter an HTTPS service URL and an access token.";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        activeRequest = cancellation;
        string? requestId = null;
        CheckButton.IsEnabled = false;
        ProgressIndicator.IsVisible = ProgressIndicator.IsRunning = true;
        try
        {
            // Keep the connection pool while using the same service. Reconnecting for every
            // button press would add network work and make requests slower.
            if (client?.BaseAddress != address)
            {
                client?.Dispose();
                client = new HttpClient
                {
                    BaseAddress = address,
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxResponseContentBufferSize = 64 * 1024
                };
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "api/status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken.Text);
            using var response = await client.SendAsync(request, cancellation.Token);
            requestId = ClientMessages.ReadReference(response);
            if (!response.IsSuccessStatusCode)
            {
                StatusLabel.Text = ClientMessages.WithReference(ClientMessages.ForStatus(response.StatusCode), requestId);
                return;
            }
            var status = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.StatusResponse, cancellation.Token)
                ?? throw new JsonException("Missing status response.");
            if (string.IsNullOrWhiteSpace(status.Message))
                throw new JsonException("Missing status message.");
            StatusLabel.Text = status.Message;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("Service request failed with status {StatusCode}. Request reference: {RequestId}", exception.StatusCode, requestId);
            StatusLabel.Text = ClientMessages.WithReference(ClientMessages.ForRequestFailure(exception), requestId);
        }
        catch (JsonException)
        {
            StatusLabel.Text = ClientMessages.WithReference(ClientMessages.InvalidReply, requestId);
        }
        catch (FormatException)
        {
            StatusLabel.Text = "The access token contains invalid characters. Copy a new token and try again.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = cancellation.IsCancellationRequested ? "Request cancelled." : ClientMessages.TimedOut;
        }
        catch (Exception exception)
        {
            // An async UI event has no caller to catch its error. Catch unexpected failures here
            // so they do not close the app. Never log the user's token or raw server response.
            logger.LogError("Unexpected client error {ErrorType}. Request reference: {RequestId}", exception.GetType().Name, requestId);
            StatusLabel.Text = ClientMessages.WithReference(ClientMessages.Unexpected, requestId);
        }
        finally
        {
            // Always restore the controls, even after a failed or cancelled request.
            activeRequest = null;
            ProgressIndicator.IsVisible = ProgressIndicator.IsRunning = false;
            CheckButton.IsEnabled = true;
        }
    }

    protected override void OnDisappearing()
    {
        activeRequest?.Cancel();
        AccessToken.Text = string.Empty;
        base.OnDisappearing();
    }
}
