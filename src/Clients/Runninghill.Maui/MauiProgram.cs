using Runninghill.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Runninghill.Maui;

/// <summary>
/// Builds the shared native app and registers its fonts, pages and platform customizations.
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// Configures native styling and dependency injection, then builds the MAUI application host.
    /// </summary>
    public static MauiApp CreateMauiApp()
    {
#if WINDOWS
        WinUI.DesktopAppearance.Configure();
#endif
        Runninghill.Contracts.AppText.SetClientLanguage(Preferences.Default.Get("runninghill.language", "en-ZA"));
        var builder = MauiApp.CreateBuilder();
        var recentLogs = new RecentLogStore();
        builder.Services.AddSingleton(recentLogs);
        builder.Logging.AddProvider(recentLogs);
        builder.Logging.AddProvider(new RollingFileLoggerProvider(Path.Combine(FileSystem.AppDataDirectory, "logs")));
        builder.UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Keep one navigation shell; create pages through DI so their dependencies are supplied.
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<MainPage>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
