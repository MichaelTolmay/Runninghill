using Runninghill.Clients;

namespace Runninghill.Maui;

/// <summary>Updates native colour resources in place, keeping existing controls, drafts, and requests alive.</summary>
internal static class ThemePalette
{
    private static bool applying;
    public static string Mode { get; private set; } = AppThemes.NormalizeMode(Preferences.Default.Get("runninghill.theme.mode", "System"));
    public static string Company { get; private set; } = Preferences.Default.Get("runninghill.theme.company", "Runninghill");
    public static AppThemeDefinition Current => AppThemes.Resolve(Company, Mode, AppInfo.Current.RequestedTheme == AppTheme.Dark);
    public static event Action? Changed;

    /// <summary>Connects the application to device theme changes once, for the application's lifetime.</summary>
    public static void Initialize(Microsoft.Maui.Controls.Application application)
    {
        application.RequestedThemeChanged += (_, _) =>
        {
            if (Mode == "System") MainThread.BeginInvokeOnMainThread(Apply);
        };
        Apply();
    }

    /// <summary>Stores only appearance choices and repaints without rebuilding the current page.</summary>
    public static void Select(string mode, string company)
    {
        Mode = AppThemes.NormalizeMode(mode);
        Company = AppThemes.Resolve(company, Mode, AppInfo.Current.RequestedTheme == AppTheme.Dark).Company;
        Preferences.Default.Set("runninghill.theme.mode", Mode);
        Preferences.Default.Set("runninghill.theme.company", Company);
        Apply();
    }

    /// <summary>Lets the OS control native chrome in System mode and publishes both colour and brush resources.</summary>
    private static void Apply()
    {
        if (applying || Microsoft.Maui.Controls.Application.Current is not { } application) return;
        applying = true;
        try
        {
            application.UserAppTheme = Mode switch { "Light" => AppTheme.Light, "Dark" => AppTheme.Dark, _ => AppTheme.Unspecified };
            foreach (var (role, value) in Current.Colors)
            {
                var color = Color.FromArgb(value);
                application.Resources["Theme" + role] = color;
                application.Resources["Theme" + role + "Brush"] = new SolidColorBrush(color);
            }
            // Resizetizer packages each original as a PNG; changing the resource keeps the page and drafts intact.
            application.Resources["ThemeIconSource"] = ImageSource.FromFile(AppThemes.IconId(Current.Id).ToLowerInvariant() + ".png");
            Changed?.Invoke();
        }
        finally { applying = false; }
    }

    /// <summary>Gives code-created controls the same live resource bindings as XAML-created controls.</summary>
    public static T WithTheme<T>(this T element, string property, string role) where T : VisualElement
    {
        var target = property switch
        {
            "BackgroundColor" => VisualElement.BackgroundColorProperty,
            "TextColor" when element is Label => Label.TextColorProperty,
            "TextColor" when element is Button => Button.TextColorProperty,
            "Color" when element is BoxView => BoxView.ColorProperty,
            "Color" when element is CheckBox => CheckBox.ColorProperty,
            "BorderColor" when element is Button => Button.BorderColorProperty,
            _ => throw new ArgumentException("Unsupported themed property: " + property, nameof(property))
        };
        element.SetDynamicResource(target, "Theme" + role);
        return element;
    }
}
