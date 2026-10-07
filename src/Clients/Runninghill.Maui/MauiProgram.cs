using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Runninghill.Sdk;

namespace Runninghill.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddRunninghillSdk($"Data Source={Path.Combine(FileSystem.AppDataDirectory, "runninghill.db")}");
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<MainPage>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
