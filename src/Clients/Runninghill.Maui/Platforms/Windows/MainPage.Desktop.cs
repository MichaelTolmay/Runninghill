using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Runninghill.Diagnostics;

namespace Runninghill.Maui;

/// <summary>
/// Adds Windows keyboard handling to the shared native word collection page.
/// </summary>
public partial class MainPage
{
    private UIElement? desktopRoot;

    /// <summary>
    /// Attaches Escape-key handling to the current native root and detaches old handlers when the root
    /// changes.
    /// </summary>
    partial void ConfigureDesktop()
    {
        HandlerChanged += (_, _) =>
        {
            // Detach before attaching a new native root; window recreation must not add
            // duplicate key handlers or keep a closed window alive.
            if (desktopRoot is not null) desktopRoot.KeyDown -= OnDesktopKeyDown;
            desktopRoot = Handler?.PlatformView as UIElement;
            if (desktopRoot is not null) desktopRoot.KeyDown += OnDesktopKeyDown;
        };
        HandlerChanging += (_, _) =>
        {
            if (desktopRoot is not null) desktopRoot.KeyDown -= OnDesktopKeyDown;
            desktopRoot = null;
        };
    }

    /// <summary>
    /// Uses Escape to close filters, dismiss a delete prompt or cancel an edit; leaves other keys to
    /// normal WinUI handling.
    /// </summary>
    private void OnDesktopKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape) return;
        OperationLog.Event(logger, "EscapePressed");
        if (TypeFilterPanel.IsVisible)
        {
            TypeFilterPanel.IsVisible = false;
            TypeFilterButton.Focus();
        }
        else if (deleteConfirmation is { IsVisible: true }) deleteConfirmation.IsVisible = false;
        else if (editingId is not null) CancelEdit();
        else return;
        args.Handled = true;
    }
}
