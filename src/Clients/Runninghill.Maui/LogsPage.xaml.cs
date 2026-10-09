using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Runninghill.Clients;
using Runninghill.Contracts;
using Runninghill.Diagnostics;

namespace Runninghill.Maui;

/// <summary>Displays bounded app or protected service logs using native, wrapping controls.</summary>
public partial class LogsPage : ContentPage
{
    private readonly RecentLogStore store;
    private readonly ILogger logger;
    private readonly HttpClient? client;
    private readonly CancellationTokenSource lifetime = new();
    private LogPage page = new([], null, 0);
    private string appliedLevel = "", appliedSearch = "";
    private bool service, busy, closed;

    /// <summary>Copies the current connection into a temporary viewer; no credential is written to disk.</summary>
    public LogsPage(RecentLogStore store, ILogger logger, Uri? serviceAddress, string token)
    {
        InitializeComponent();
        this.store = store;
        this.logger = logger;
        if (serviceAddress is not null)
            client = new HttpClient { BaseAddress = serviceAddress, Timeout = ClientMessages.RequestTimeout, MaxResponseContentBufferSize = 256 * 1024 };
        // Show only the host, not a possible private query or credentials embedded in the address.
        ServiceAddress.Text = serviceAddress is null ? "Enter a valid service URL on the collection screen first." : $"Service: {serviceAddress.Host}";
        Token.Text = token;
        Source.ItemsSource = new[] { "This app", "Service" };
        Source.SelectedIndex = 0;
        Severity.ItemsSource = new[] { "All levels", "Information", "Warning", "Error", "Critical", "Debug", "Trace" };
        Severity.SelectedIndex = 0;
        OperationLog.Event(logger, "LogViewerOpened");
        page = store.Read();
        Render();
    }

    /// <summary>Shows the permission field only when service logs are selected.</summary>
    private void OnSourceChanged(object? sender, EventArgs e) => ServiceSettings.IsVisible = Source.SelectedIndex == 1;

    /// <summary>Applies the current filters and shows the newest matching records.</summary>
    private async void OnRefresh(object? sender, EventArgs e)
    {
        if (busy || closed) return;
        service = Source.SelectedIndex == 1;
        appliedLevel = Severity.SelectedIndex > 0 ? (string)Severity.SelectedItem : "";
        appliedSearch = Search.Text?.Trim() ?? "";
        await LoadAsync(0);
    }

    /// <summary>Clears the filters without changing the selected source.</summary>
    private void OnReset(object? sender, EventArgs e)
    {
        Severity.SelectedIndex = 0; Search.Text = ""; OnRefresh(sender, e);
    }

    /// <summary>Uses the last visible record as a bookmark, so new events cannot shift older pages.</summary>
    private async void OnOlder(object? sender, EventArgs e)
    {
        if (page.NextBefore is { } before) await LoadAsync(before);
    }

    /// <summary>Reads at most twenty records and turns network, permission and format errors into guidance.</summary>
    private async Task LoadAsync(long before)
    {
        if (busy || closed) return;
        busy = true; SetBusy();
        Feedback.TextColor = Color.FromArgb("#315C8C");
        Feedback.Text = "Loading logs…";
        try
        {
            if (!service) page = store.Read(before, appliedLevel, appliedSearch);
            else
            {
                if (client is null) throw new ViewerFailure("Enter a valid service URL on the collection screen, then reopen Logs.");
                if (string.IsNullOrWhiteSpace(Token.Text)) throw new ViewerFailure("Enter an access token with logs.read permission, then refresh.");
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"api/logs?before={before}&level={Uri.EscapeDataString(appliedLevel)}&search={Uri.EscapeDataString(appliedSearch)}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token.Text.Trim());
                using var response = await client.SendAsync(request, lifetime.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = response.StatusCode == HttpStatusCode.Forbidden
                        ? "This token cannot read service logs. Ask an administrator for logs.read permission."
                        : response.StatusCode == HttpStatusCode.NotFound ? "This service does not provide the log viewer yet. Deploy the updated service and try again."
                        : ClientMessages.ForStatus(response.StatusCode);
                    throw new ViewerFailure(ClientMessages.WithReference(detail, ClientMessages.ReadReference(response)));
                }
                page = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.LogPage, lifetime.Token) ?? throw new JsonException();
                if (page.Items is null || page.Items.Length > RecentLogStore.PageSize || page.Items.Any(entry => entry is null || entry.Level is null)) throw new JsonException();
            }
            Feedback.Text = $"{page.Items.Length} events shown. Refresh to check for new events.";
        }
        catch (ViewerFailure exception) { Fail(exception.Message); }
        catch (HttpRequestException exception) { Fail(ClientMessages.ForRequestFailure(exception)); }
        catch (JsonException) { Fail(ClientMessages.InvalidReply); }
        catch (FormatException) { Fail("The access token is not valid. Copy a fresh token and refresh."); }
        catch (OperationCanceledException) { Fail("Log loading was cancelled or timed out. Try refreshing."); }
        catch (Exception exception)
        {
            OperationLog.Event(logger, "LogViewerFailed:" + exception.GetType().Name);
            Fail("Could not load logs. Refresh and share the time of this error with support if it continues.");
        }
        finally
        {
            busy = false;
            if (!closed) { Render(); SetBusy(); }
        }
    }

    /// <summary>Removes stale results after an error and displays safe recovery instructions.</summary>
    private void Fail(string detail)
    {
        page = new([], null, 0);
        if (closed) return;
        Feedback.Text = detail; Feedback.TextColor = Color.FromArgb("#B42337");
    }

    /// <summary>Draws one small page with wrapping messages, keeping large log histories off the UI thread.</summary>
    private void Render()
    {
        ResultTitle.Text = service ? "Service events" : "App events";
        Rows.Clear();
        if (page.Items.Length == 0) Rows.Add(new Label { Text = "No matching events. Try another severity or search, perform an action, then refresh." });
        foreach (var entry in page.Items)
        {
            var details = new VerticalStackLayout { Spacing = 6 };
            details.Add(new Label { Text = $"{entry.Level} · {entry.Timestamp.ToLocalTime():dd MMM yyyy HH:mm:ss.fff} · Event {entry.EventId}", FontAttributes = FontAttributes.Bold });
            details.Add(new Label { Text = entry.Category, FontSize = 12, LineBreakMode = LineBreakMode.CharacterWrap });
            details.Add(new Label { Text = entry.Message, LineBreakMode = LineBreakMode.CharacterWrap });
            Rows.Add(new Border { Content = details });
        }
        Count.Text = $"{page.Items.Length} shown · {page.RetainedCount} retained";
        Older.IsEnabled = page.NextBefore is not null && !busy;
    }

    /// <summary>Prevents overlapping reads and keeps progress visible while a service reply is pending.</summary>
    private void SetBusy()
    {
        Filters.IsEnabled = Newest.IsEnabled = !busy;
        Older.IsEnabled = !busy && page.NextBefore is not null;
        Loading.IsVisible = Loading.IsRunning = busy;
    }

    /// <summary>Returns to the existing collection page without replacing its draft or connection.</summary>
    private async void OnClose(object? sender, EventArgs e)
    {
        try { await Navigation.PopModalAsync(); }
        catch (Exception exception)
        {
            OperationLog.Event(logger, "LogViewerCloseFailed:" + exception.GetType().Name);
            Fail("Could not close this screen. Try Back again.");
        }
    }

    /// <summary>Cancels reads and clears the temporary token when either native Back or the close button is used.</summary>
    protected override void OnDisappearing()
    {
        if (!closed)
        {
            closed = true; lifetime.Cancel(); client?.Dispose(); lifetime.Dispose(); Token.Text = "";
            OperationLog.Event(logger, "LogViewerClosed");
        }
        base.OnDisappearing();
    }

    /// <summary>Carries only a deliberately user-friendly error, never a raw server exception.</summary>
    private sealed class ViewerFailure(string message) : Exception(message);
}
