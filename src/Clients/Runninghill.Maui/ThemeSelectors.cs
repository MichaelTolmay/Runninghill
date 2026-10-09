using Runninghill.Clients;
using Runninghill.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Runninghill.Maui;

/// <summary>Shows translated brightness and company choices side by side using native pickers.</summary>
public sealed class ThemeSelectors : Grid
{
    private readonly Picker system = new(), app = new();
    private readonly Label systemLabel = new(), appLabel = new();
    private readonly Label warning = new() { FontSize = 12, IsVisible = AppThemes.HasInvalidFiles };
    private AppThemeDefinition[] choices = [];
    private bool updating;

    /// <summary>Builds a compact two-column selector that can shrink on phones.</summary>
    public ThemeSelectors()
    {
        ColumnDefinitions = [new(GridLength.Star), new(new GridLength(1.4, GridUnitType.Star))];
        RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)];
        ColumnSpacing = 8;
        system.FontSize = app.FontSize = systemLabel.FontSize = appLabel.FontSize = 12;
        system.BackgroundColor = app.BackgroundColor = Colors.Transparent;
        system.SetDynamicResource(Picker.TextColorProperty, "ThemeText");
        app.SetDynamicResource(Picker.TextColorProperty, "ThemeText");
        systemLabel.SetDynamicResource(Label.TextColorProperty, "ThemeText");
        appLabel.SetDynamicResource(Label.TextColorProperty, "ThemeText");
        Add(new VerticalStackLayout { Spacing = 7, Children = { systemLabel, system } });
        this.Add(new VerticalStackLayout { Spacing = 7, Children = { appLabel, app } }, 1);
        warning.SetDynamicResource(Label.TextColorProperty, "ThemeErrorText");
        this.Add(warning, 0, 1); Grid.SetColumnSpan((BindableObject)warning, 2);
        system.SelectedIndexChanged += ChangeMode;
        app.SelectedIndexChanged += ChangeApp;
        Loaded += Attach;
        Unloaded += Detach;
        Refresh();
    }

    /// <summary>Observes theme and language changes only while this control is on screen.</summary>
    private void Attach(object? sender, EventArgs args)
    {
        Detach(sender, args);
        ThemePalette.Changed += Refresh;
        AppText.LanguageChanged += Refresh;
        Refresh();
    }

    /// <summary>Removes static event subscriptions so closed pages can be collected.</summary>
    private void Detach(object? sender, EventArgs args)
    {
        ThemePalette.Changed -= Refresh;
        AppText.LanguageChanged -= Refresh;
    }

    /// <summary>Refreshes display labels without turning a programmatic selection into another user action.</summary>
    private void Refresh()
    {
        updating = true;
        try
        {
            systemLabel.Text = system.Title = AppText.T("System theme");
            appLabel.Text = app.Title = AppText.T("App theme");
            warning.Text = AppText.T("Some app themes could not be loaded. Ask your administrator to check the theme files.");
            SemanticProperties.SetDescription(system, systemLabel.Text);
            SemanticProperties.SetDescription(app, appLabel.Text);
            system.ItemsSource = new[] { AppText.T("Light"), AppText.T("Dark"), AppText.T("System") };
            system.SelectedIndex = ThemePalette.Mode switch { "Light" => 0, "Dark" => 1, _ => 2 };
            var selected = ThemePalette.Current;
            choices = AppThemes.All.Where(theme => theme.Mode == selected.Mode).ToArray();
            app.ItemsSource = choices.Select(theme => theme.Id).ToArray();
            app.SelectedIndex = Array.FindIndex(choices, theme => theme.Id == selected.Id);
        }
        finally { updating = false; }
    }

    /// <summary>Changes brightness while retaining the company and any unsaved collection work.</summary>
    private void ChangeMode(object? sender, EventArgs args)
    {
        if (updating || system.SelectedIndex < 0) return;
        ThemePalette.Select(new[] { "Light", "Dark", "System" }[system.SelectedIndex], ThemePalette.Company);
        RecordChange("SystemThemeChanged");
    }

    /// <summary>Selects the company represented by the chosen filename.</summary>
    private void ChangeApp(object? sender, EventArgs args)
    {
        if (updating || app.SelectedIndex < 0) return;
        ThemePalette.Select(ThemePalette.Mode, choices[app.SelectedIndex].Company);
        RecordChange("AppThemeChanged");
    }

    /// <summary>Records a named UI event without recording user content or credentials.</summary>
    private void RecordChange(string name)
    {
        var logger = Handler?.MauiContext?.Services.GetService<Microsoft.Extensions.Logging.ILogger<ThemeSelectors>>();
        if (logger is not null) Runninghill.Diagnostics.OperationLog.Event(logger, name);
    }
}
