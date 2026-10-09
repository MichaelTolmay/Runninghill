using Runninghill.Contracts;

namespace Runninghill.Maui;

/// <summary>Offers the same five languages on phones and native desktop windows.</summary>
public sealed class LanguagePicker : Picker
{
    /// <summary>Restores the selected language before subscribing, avoiding a false change during startup.</summary>
    public LanguagePicker()
    {
        ItemsSource = AppText.LanguageNames.ToArray();
        SelectedIndex = Array.IndexOf(AppText.Languages.ToArray(), AppText.Language);
        SetBinding(TitleProperty, new TranslateExtension { Key = "Text_89b86ab0e66f" }.ProvideValue(null!));
        SelectedIndexChanged += OnLanguageChanged;
    }

    /// <summary>Persists only a language code; credentials remain in memory.</summary>
    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        if (SelectedIndex < 0) return;
        var language = AppText.Languages[SelectedIndex];
        Preferences.Default.Set("runninghill.language", language);
        AppText.SetClientLanguage(language);
    }
}
