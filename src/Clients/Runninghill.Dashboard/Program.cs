using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Photino.Blazor;
using Runninghill.Contracts;
using Runninghill.Diagnostics;

namespace Runninghill.Dashboard;

/// <summary>Starts one native desktop window hosting only the monitoring dashboard.</summary>
internal static class Program
{
    /// <summary>Starts the window and records a safe diagnostic if native startup fails before Blazor can render.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception error)
        {
            var detail = "RH-DASHBOARD-STARTUP · " + error.GetType().Name + Environment.NewLine +
                AppText.T("Close and reopen the dashboard. Share the error code and error type with support. Check the certificate file if one was configured.");
            Console.Error.WriteLine(detail);
            // WinExe has no console when opened from Explorer. Keep the same safe diagnostic
            // in the user's application-data folder, without exception text or credentials.
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Runninghill", "Dashboard");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "startup-error.log"), detail);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return 1;
        }
    }

    /// <summary>Registers shared resources and HTTP monitoring; the UI never connects to a database.</summary>
    private static void Run(string[] args)
    {
        // Resolve paths before Photino changes the working directory. An explicit choice
        // always wins; never silently fall back if the user supplied a missing/wrong file.
        var configuredCertificate = Environment.GetEnvironmentVariable("RUNNINGHILL_DASHBOARD_CA_CERT");
        var localCertificate = string.IsNullOrWhiteSpace(configuredCertificate);
        var certificatePath = localCertificate
            ? DashboardClient.FindLocalCertificate(AppContext.BaseDirectory, Directory.GetCurrentDirectory())
            : Path.GetFullPath(configuredCertificate!);
        // Photino serves wwwroot relative to the working directory. Launching from a shortcut
        // or the repository root must work just like launching beside the executable.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        var logs = new RecentLogStore();
        builder.Services.AddSingleton(logs);
        builder.Services.AddLogging(logging => logging.AddProvider(logs).AddConsole());
        HttpClient? http = null;
        builder.Services.AddSingleton(_ => http = DashboardClient.CreateHttpClient(certificatePath, localhostOnly: localCertificate));
        builder.Services.AddSingleton<DashboardClient>();
        builder.RootComponents.Add<App>("#app");
        var app = builder.Build();
        // Photino's verbose bridge trace includes rendered input values. Disable it before
        // the window loads so access tokens cannot be copied into console diagnostics.
        app.MainWindow.SetLogVerbosity(0).SetTitle("Runninghill · " + AppText.T("Monitoring dashboard"))
            .SetWidth(1280).SetHeight(900).Center();
        void UpdateTitle() => app.MainWindow.SetTitle("Runninghill · " + AppText.T("Monitoring dashboard"));
        AppText.LanguageChanged += UpdateTitle;
        try { app.Run(); }
        finally
        {
            AppText.LanguageChanged -= UpdateTitle;
            // Stop pooled connections even when the native window closes before Blazor can
            // dispose its components. Do not dispatch more UI work to a closed native window.
            http?.Dispose();
        }
    }
}
