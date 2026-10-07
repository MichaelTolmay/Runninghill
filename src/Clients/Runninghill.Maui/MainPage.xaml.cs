using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Runninghill.Contracts;

namespace Runninghill.Maui;

public partial class MainPage : ContentPage
{
    private readonly ILogger<MainPage> logger;
    private HttpClient? client;

    public MainPage(ILogger<MainPage> logger)
    {
        InitializeComponent();
        this.logger = logger;
    }

    private async void OnCheckClicked(object? sender, EventArgs e)
    {
        if (!Uri.TryCreate(ServiceUrl.Text, UriKind.Absolute, out var address) || address.Scheme != "https" ||
            string.IsNullOrWhiteSpace(AccessToken.Text))
        {
            StatusLabel.Text = "Enter an HTTPS service URL and an access token.";
            return;
        }
        // Reuse the connection pool until the user selects a different service.
        if (client?.BaseAddress != address)
        {
            client?.Dispose();
            client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
        }
        CheckButton.IsEnabled = false;
        ProgressIndicator.IsVisible = ProgressIndicator.IsRunning = true;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken.Text);
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var status = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.StatusResponse)
                ?? throw new HttpRequestException("The service returned an empty response.");
            StatusLabel.Text = status.Message;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("Service request failed with status {StatusCode}.", exception.StatusCode);
            StatusLabel.Text = "Check your access token, permissions, and service availability.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "The request timed out. Please try again.";
        }
        finally
        {
            ProgressIndicator.IsVisible = ProgressIndicator.IsRunning = false;
            CheckButton.IsEnabled = true;
        }
    }
}
