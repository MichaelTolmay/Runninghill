using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Media;

namespace Runninghill.Maui.WinUI;

// MAUI supplies real WinUI controls. Keep their keyboard, focus and accessibility behavior,
// while giving text fields the same outline and rounded corners as the website.
/// <summary>
/// Styles native Windows text fields and pickers to match the website while retaining WinUI
/// interaction behaviour.
/// </summary>
internal static class DesktopAppearance
{
    /// <summary>
    /// Adds native entry and picker mappings for rounded borders and matching input padding.
    /// </summary>
    public static void Configure()
    {
        EntryHandler.Mapper.AppendToMapping("WebsiteAppearance", (handler, _) =>
        {
            handler.PlatformView.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(6);
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
            handler.PlatformView.BorderBrush = InputBorder();
            handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(12, 10, 12, 10);
        });
        PickerHandler.Mapper.AppendToMapping("WebsiteAppearance", (handler, _) =>
        {
            handler.PlatformView.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(6);
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
            handler.PlatformView.BorderBrush = InputBorder();
            handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(12, 10, 12, 10);
        });
    }

    /// <summary>
    /// Creates the shared input-outline colour as a native WinUI brush.
    /// </summary>
    private static SolidColorBrush InputBorder() => new(global::Windows.UI.Color.FromArgb(255, 186, 197, 211));
}
