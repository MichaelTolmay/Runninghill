using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Runninghill.Clients;
using Runninghill.Contracts;
using Runninghill.Diagnostics;
using static Runninghill.Contracts.AppText;

namespace Runninghill.Dashboard;

/// <summary>Owns one monitoring session in one dashboard window; no editing or collection pages are hosted.</summary>
public partial class Dashboard
{
    [Inject] private DashboardClient Client { get; set; } = null!;
    [Inject] private RecentLogStore Logs { get; set; } = null!;
    [Inject] private IJSRuntime JavaScript { get; set; } = null!;
    private static readonly string[] LogLevels = ["Information", "Warning", "Error", "Critical", "Debug", "Trace"];
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? session;
    private Task polling = Task.CompletedTask, loading = Task.CompletedTask;
    private bool running, stopping, loadBusy, logBusy, disposed;
    private string intervalText = "10", serviceUrl = "https://localhost:5443/", websiteUrl = "https://localhost:5443/", token = "";
    private string activeToken = "", message = "", statisticsError = "", logError = "";
    private Uri activeService = new("https://localhost:5443/");
    private long checks, failures, before;
    private DateTimeOffset? lastCheck;
    private ProbeResult? serviceHealth, websiteHealth;
    private CollectionStatistics? statistics;
    private LoadReport? load;
    private string logSource = "app", logLevel = "", logSearch = "";
    private string appliedSource = "app", appliedLevel = "", appliedSearch = "";
    private LogPage logPage = new([], null, 0);
    private string mode = "System", company = "Runninghill";
    private AppThemeDefinition theme = AppThemes.Resolve("Runninghill", "Light", false);

    /// <summary>Restores appearance only; addresses and tokens are never written to browser storage.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            AppText.SetClientLanguage(await JavaScript.InvokeAsync<string>("dashboard.language"));
            using var saved = JsonDocument.Parse(await JavaScript.InvokeAsync<string>("runninghillTheme.state"));
            mode = AppThemes.NormalizeMode(saved.RootElement.GetProperty("mode").GetString());
            company = saved.RootElement.GetProperty("company").GetString() ?? "Runninghill";
            await ApplyThemeAsync();
            Event("Information", T("Dashboard opened."));
            await RefreshLogsAsync();
            StateHasChanged();
        }
        catch (Exception error) { Unexpected(error); StateHasChanged(); }
    }

    /// <summary>Validates editable settings, then starts one sequential polling loop without blocking the UI.</summary>
    private async Task StartAsync()
    {
        if (running || stopping || loadBusy) return;
        if (!int.TryParse(intervalText, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds is < 1 or > 3600)
        { message = T("Enter a whole number of seconds from 1 to 3,600."); return; }
        if (!DashboardClient.TryBaseAddress(serviceUrl, out var service) || !DashboardClient.TryBaseAddress(websiteUrl, out var website))
        { message = T("Enter an HTTPS address, or HTTP on localhost. Do not include passwords, query strings or fragments."); return; }
        if (token.Length > 8192 || token.Any(char.IsControl))
        { message = T("The access token is not valid. Copy a fresh token and refresh."); return; }
        session?.Dispose();
        session = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        activeService = service; activeToken = token.Trim();
        statistics = null; statisticsError = ""; load = null; message = "";
        serviceHealth = websiteHealth = null; lastCheck = null;
        running = true;
        Event("Information", T("Monitoring started."));
        await RefreshLogsAsync();
        polling = PollAsync(service, website, seconds, session.Token);
    }

    /// <summary>Checks the two endpoints concurrently, but never overlaps cycles or queues missed ticks.</summary>
    private async Task PollAsync(Uri service, Uri website, int seconds, CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var health = await Task.WhenAll(Client.ProbeAsync(new Uri(service, "health/ready"), cancellation),
                    Client.ProbeAsync(new Uri(website, "health/live"), cancellation));
                cancellation.ThrowIfCancellationRequested();
                serviceHealth = health[0]; websiteHealth = health[1];
                checks += 2; failures += health.Count(probe => !probe.Healthy); lastCheck = DateTimeOffset.UtcNow;
                Event(serviceHealth.Healthy ? "Information" : "Error", T("Service") + ": " + serviceHealth.Detail);
                Event(websiteHealth.Healthy ? "Information" : "Error", T("Website") + ": " + websiteHealth.Detail);
                var totals = await Client.StatisticsAsync(service, activeToken, cancellation);
                statistics = totals.Data; statisticsError = totals.Error;
                // A failed refresh clears totals instead of showing old numbers as current.
                if (totals.Error.Length > 0) Event("Warning", totals.Error);
                if (before == 0 && !logBusy) await LoadLogsAsync(0, cancellation);
                if (!disposed) await InvokeAsync(StateHasChanged);
                // Delay after completion, rather than accumulating ticks when the service is slow.
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { Unexpected(error); session?.Cancel(); running = false; if (!disposed) await InvokeAsync(StateHasChanged); }
    }

    /// <summary>Cancels health checks and load requests and waits for them to stop before enabling new settings.</summary>
    private async Task StopAsync()
    {
        if (stopping) return;
        stopping = true;
        session?.Cancel();
        await Task.WhenAll(polling, loading);
        running = false; stopping = false; activeToken = "";
        Event("Information", T("Monitoring stopped."));
        if (appliedSource == "app") await LoadLogsAsync(before, lifetime.Token);
    }

    /// <summary>Starts a bounded load sample only on explicit request; monitoring totals count health probes separately.</summary>
    private Task RunLoadAsync()
    {
        if (loadBusy || !running || session is null) return Task.CompletedTask;
        loading = SampleLoadAsync(session.Token);
        return loading;
    }

    /// <summary>Publishes measured latency and throughput, including failures; Stop cancels in-flight requests.</summary>
    private async Task SampleLoadAsync(CancellationToken cancellation)
    {
        loadBusy = true; load = null;
        Event("Information", T("Load test started."));
        try
        {
            load = await Client.LoadAsync(activeService, activeToken, cancellation);
            Event(load.Failures == 0 ? "Information" : "Warning", T("Load test completed.") + " " + F($"{load.Requests} requests; {load.Failures} failures; {load.RequestsPerSecond:F1} requests/second."));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Event("Information", T("Load test cancelled.")); }
        catch (Exception error) { Unexpected(error); }
        finally { loadBusy = false; }
    }

    /// <summary>Applies log filters and returns to the newest retained records.</summary>
    private Task RefreshLogsAsync()
    {
        if (logBusy) return Task.CompletedTask;
        appliedSource = logSource; appliedLevel = logLevel; appliedSearch = logSearch.Trim();
        return LoadLogsAsync(0, lifetime.Token);
    }

    /// <summary>Keeps the selected source and filters when viewing an older retained page.</summary>
    private Task OlderLogsAsync() => logPage.NextBefore is { } next ? LoadLogsAsync(next, lifetime.Token) : Task.CompletedTask;

    /// <summary>Reads bounded diagnostic pages. Permission failures clear previous service results.</summary>
    private async Task LoadLogsAsync(long cursor, CancellationToken cancellation)
    {
        if (logBusy) return;
        logBusy = true; before = cursor; logError = "";
        try
        {
            if (appliedSource == "app") logPage = Logs.Read(cursor, appliedLevel, appliedSearch);
            else if (DashboardClient.TryBaseAddress(serviceUrl, out var service))
            {
                var reply = await Client.LogsAsync(running ? activeService : service, running ? activeToken : token,
                    cursor, appliedLevel, appliedSearch, cancellation);
                logPage = reply.Data ?? new([], null, 0); logError = reply.Error;
            }
            else { logPage = new([], null, 0); logError = T("Enter an HTTPS address, or HTTP on localhost. Do not include passwords, query strings or fragments."); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { logPage = new([], null, 0); Unexpected(error); }
        finally { logBusy = false; }
    }

    /// <summary>Switches brightness without restarting monitoring or losing credentials.</summary>
    private async Task ChangeModeAsync(ChangeEventArgs args) { mode = AppThemes.NormalizeMode(args.Value?.ToString()); await ApplyThemeAsync(); }

    /// <summary>Chooses the company that supplies the active light/dark palette.</summary>
    private async Task ChangeCompanyAsync(ChangeEventArgs args) { company = AppThemes.All.FirstOrDefault(item => item.Id == args.Value?.ToString())?.Company ?? "Runninghill"; await ApplyThemeAsync(); }

    /// <summary>Paints the shared palette; the shared JavaScript also responds to OS brightness changes.</summary>
    private async Task ApplyThemeAsync()
    {
        using var state = JsonDocument.Parse(await JavaScript.InvokeAsync<string>("runninghillTheme.state"));
        theme = AppThemes.Resolve(company, mode, state.RootElement.GetProperty("systemDark").GetBoolean());
        await JavaScript.InvokeVoidAsync("runninghillTheme.apply", mode, company, theme.Id, theme.Json);
    }

    /// <summary>Changes the five-language resource catalogue and remembers only the language preference.</summary>
    private async Task ChangeLanguageAsync(ChangeEventArgs args)
    {
        AppText.SetClientLanguage(args.Value?.ToString());
        await JavaScript.InvokeVoidAsync("dashboard.setLanguage", AppText.Language);
        Event("Information", T("Language changed."));
    }

    /// <summary>Adds a safe bounded diagnostic record; tokens and endpoint addresses are deliberately omitted.</summary>
    private void Event(string level, string text) => Logs.Add(DateTimeOffset.UtcNow, level, "Runninghill.Dashboard", 0, text);

    /// <summary>Displays a correlation reference without exposing exception contents.</summary>
    private void Unexpected(Exception error)
    {
        var reference = Guid.NewGuid().ToString("N");
        message = ClientMessages.WithReference(ClientMessages.ForUnexpected(error), reference);
        Event("Error", message);
    }

    /// <summary>Distinguishes an unmeasured endpoint from a failed endpoint.</summary>
    private static string HealthLabel(ProbeResult? probe) => probe is null ? T("Not checked") : probe.Healthy ? T("Healthy") : T("Unavailable");

    /// <summary>Formats latency with the user's number conventions.</summary>
    private static string Latency(ProbeResult? probe) => probe is null ? "—" : probe.Milliseconds.ToString("N0", AppText.Culture) + " ms";

    /// <summary>Caps only the gauge drawing; the displayed measured latency is never truncated.</summary>
    private string GaugeDegrees() => Math.Clamp((load?.P95Milliseconds ?? 0) / 2000 * 360, 0, 360).ToString(CultureInfo.InvariantCulture);

    /// <summary>Shows an empty gauge until a test has produced an actual measurement.</summary>
    private string LoadValue() => load is null || load.Requests == 0 ? "—" : load.P95Milliseconds.ToString("N0", AppText.Culture) + " ms";

    /// <summary>Cancels background work and removes tokens when the single window closes.</summary>
    public async ValueTask DisposeAsync()
    {
        disposed = true; lifetime.Cancel(); session?.Cancel();
        await Task.WhenAll(polling, loading);
        session?.Dispose(); lifetime.Dispose(); token = activeToken = "";
    }
}
