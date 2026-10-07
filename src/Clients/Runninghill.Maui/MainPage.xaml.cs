using Microsoft.Extensions.Logging;
using Runninghill.Sdk;

namespace Runninghill.Maui;

public partial class MainPage : ContentPage
{
    private readonly IRunninghillSdk sdk;
    private readonly ILogger<MainPage> logger;

    public MainPage(IRunninghillSdk sdk, ILogger<MainPage> logger)
    {
        InitializeComponent();
        this.sdk = sdk;
        this.logger = logger;
    }

    private async void OnCheckClicked(object? sender, EventArgs e)
    {
        CheckButton.IsEnabled = false;
        ProgressIndicator.IsVisible = ProgressIndicator.IsRunning = true;
        try
        {
            StatusLabel.Text = await sdk.GetStatusAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Application status check failed.");
            StatusLabel.Text = "The connection could not be verified. Please try again.";
        }
        finally
        {
            ProgressIndicator.IsVisible = ProgressIndicator.IsRunning = false;
            CheckButton.IsEnabled = true;
        }
    }
}
