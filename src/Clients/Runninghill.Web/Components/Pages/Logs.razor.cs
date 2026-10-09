using static Runninghill.Contracts.AppText;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Runninghill.Clients;
using Runninghill.Contracts;
using Runninghill.Diagnostics;

namespace Runninghill.Web.Components.Pages;

/// <summary>Displays local or permission-protected service diagnostics without polling or changing stored data.</summary>
public partial class Logs
{
    [Inject] private RecentLogStore Store { get; set; } = null!;
    [Inject] private HttpClient Client { get; set; } = null!;
    [Inject] private ILogger<Logs> Logger { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Parameter] public string InitialToken { get; set; } = "";
    [Parameter] public EventCallback OnClose { get; set; }
    private static readonly string[] Levels = ["Information", "Warning", "Error", "Critical", "Debug", "Trace"];
    private readonly CancellationTokenSource lifetime = new();
    private LogPage page = new([], null, 0);
    private string source = "app", level = "", search = "", token = "";
    private string appliedSource = "app", appliedLevel = "", appliedSearch = "";
    private string message = "";
    private bool busy, error;

    /// <summary>Shows a local snapshot immediately and copies the current connection token only in memory.</summary>
    protected override void OnInitialized()
    {
        AppText.LanguageChanged += RefreshLanguage;
        token = InitialToken;
        OperationLog.Event(Logger, "LogViewerOpened");
        page = Store.Read();
        message = T("Showing recent events from this browser tab.");
    }

    /// <summary>Applies the visible filters and starts again from the newest retained record.</summary>
    private Task RefreshAsync()
    {
        appliedSource = source; appliedLevel = level; appliedSearch = search.Trim();
        return LoadAsync(0);
    }

    /// <summary>Clears the severity and search while retaining the selected log source.</summary>
    private Task ResetAsync() { level = search = ""; return RefreshAsync(); }

    /// <summary>Loads older matches with the same applied filters and a stable record-ID bookmark.</summary>
    private Task OlderAsync() => page.NextBefore is { } before ? LoadAsync(before) : Task.CompletedTask;

    /// <summary>Loads a bounded page, clearing stale results on failure and displaying safe recovery guidance.</summary>
    private async Task LoadAsync(long before)
    {
        if (busy) return;
        busy = true; error = false; message = T("Loading logs…");
        try
        {
            if (appliedSource == "app") page = Store.Read(before, appliedLevel, appliedSearch);
            else
            {
                if (string.IsNullOrWhiteSpace(token)) throw new ViewerFailure(T("Enter an access token with logs.read permission, then refresh."));
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"api/logs?before={before}&level={Uri.EscapeDataString(appliedLevel)}&search={Uri.EscapeDataString(appliedSearch)}");
                request.Headers.AcceptLanguage.ParseAdd(Runninghill.Contracts.AppText.Language);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
                using var response = await Client.SendAsync(request, lifetime.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = response.StatusCode == HttpStatusCode.Forbidden
                        ? ClientMessages.WithStatus(T("This token cannot read service logs. Ask an administrator for logs.read permission."), response.StatusCode)
                        : response.StatusCode == HttpStatusCode.NotFound ? ClientMessages.WithStatus(T("This service does not provide the log viewer yet. Deploy the updated service and try again."), response.StatusCode)
                        : ClientMessages.ForStatus(response.StatusCode);
                    throw new ViewerFailure(ClientMessages.WithReference(detail, ClientMessages.ReadReference(response)));
                }
                page = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.LogPage, lifetime.Token) ?? throw new JsonException();
                if (page.Items is null || page.Items.Length > RecentLogStore.PageSize || page.Items.Any(entry => entry is null || entry.Level is null)) throw new JsonException();
            }
            message = F($"{page.Items.Length} events shown. Refresh to check for new events.");
        }
        catch (OperationCanceledException) { Fail(T("Log loading was cancelled or timed out. Try refreshing.")); }
        catch (HttpRequestException exception) { Fail(ClientMessages.ForRequestFailure(exception)); }
        catch (JsonException) { Fail(ClientMessages.InvalidReply); }
        catch (FormatException) { Fail(T("The access token is not valid. Copy a fresh token and refresh.")); }
        catch (ViewerFailure exception) { Fail(exception.Message); }
        catch (Exception exception)
        {
            var reference = Guid.NewGuid().ToString("N");
            Logger.LogError("Log viewer failed. Reference: {Reference}; type: {ErrorType}; stack: {StackTrace}",
                reference, exception.GetType().Name, exception.StackTrace);
            Fail(ClientMessages.WithReference(ClientMessages.ForUnexpected(exception), reference));
        }
        finally { busy = false; }
    }

    /// <summary>Clears old results so a failed permission check cannot leave a misleading service page visible.</summary>
    private void Fail(string detail) { error = true; message = detail; page = new([], null, 0); }

    /// <summary>Returns to the preserved collection screen, or navigates home for a standalone viewer.</summary>
    private async Task CloseAsync()
    {
        if (OnClose.HasDelegate) await OnClose.InvokeAsync();
        else Navigation.NavigateTo("/");
    }

    /// <summary>Carries only a deliberately user-friendly message, never a raw server exception.</summary>
    private sealed class ViewerFailure(string message) : Exception(message);

    /// <summary>Refreshes translated labels while retaining the draft, filters, and connection.</summary>
    private void RefreshLanguage() => _ = InvokeAsync(() =>
    {
        message = T("Language changed."); error = false; StateHasChanged();
    });

    /// <summary>Cancels outstanding reads and removes the viewer's in-memory token when closed.</summary>
    public void Dispose() { AppText.LanguageChanged -= RefreshLanguage; lifetime.Cancel(); lifetime.Dispose(); token = ""; }
}
