using static Runninghill.Contracts.AppText;
using Microsoft.Extensions.Logging;
using Runninghill.Diagnostics;

namespace Runninghill.Maui;

/// <summary>
/// Owns the native application, its shared navigation shell and its remembered colour theme.
/// </summary>
public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly AppShell shell;
    private readonly ILogger<App> logger;

    /// <summary>
    /// Loads the app resources and keeps the supplied navigation shell for the main window.
    /// </summary>
    public App(AppShell shell, ILogger<App> logger)
    {
        InitializeComponent();
        ThemePalette.Initialize(this);
        this.shell = shell;
        this.logger = logger;
    }

    /// <summary>
    /// Creates the main window and gives Windows a resizable desktop starting size.
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(shell) { Title = T("Word collection · Runninghill") };
        if (OperatingSystem.IsWindows())
        {
            window.Width = 1280;
            window.Height = 850;
            window.MinimumWidth = 320;
            window.MinimumHeight = 480;
        }
        window.Created += (_, _) => OperationLog.Event(logger, "WindowCreated");
        window.Activated += (_, _) => OperationLog.Event(logger, "WindowActivated");
        window.Deactivated += (_, _) => OperationLog.Event(logger, "WindowDeactivated");
        window.Stopped += (_, _) => OperationLog.Event(logger, "WindowStopped");
        window.Resumed += (_, _) => OperationLog.Event(logger, "WindowResumed");
        window.Destroying += (_, _) => OperationLog.Event(logger, "WindowDestroying");
        return window;
    }
}
