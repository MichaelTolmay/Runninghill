using System.Globalization;
using System.Resources;

namespace Runninghill.Contracts;

/// <summary>Reads compiled translations; user-created words and protocol values never pass through this lookup.</summary>
public static partial class AppText
{
    // All dictionaries live in this assembly. Switching languages therefore needs no network
    // download, even in an offline browser or a trimmed native application.
    private static readonly IReadOnlyDictionary<string, ResourceManager> Resources =
        new Dictionary<string, ResourceManager>(StringComparer.Ordinal)
        {
            ["en-ZA"] = new("Runninghill.Contracts.Resources.Text", typeof(AppText).Assembly),
            ["af-ZA"] = new("Runninghill.Contracts.Resources.Text_af_ZA", typeof(AppText).Assembly),
            ["xh-ZA"] = new("Runninghill.Contracts.Resources.Text_xh_ZA", typeof(AppText).Assembly),
            ["zu-ZA"] = new("Runninghill.Contracts.Resources.Text_zu_ZA", typeof(AppText).Assembly),
            ["tn-ZA"] = new("Runninghill.Contracts.Resources.Text_tn_ZA", typeof(AppText).Assembly)
        };
    private static CultureInfo? clientCulture;
    public static IReadOnlyList<string> Languages { get; } = Array.AsReadOnly(new[] { "en-ZA", "af-ZA", "xh-ZA", "zu-ZA", "tn-ZA" });
    public static IReadOnlyList<string> LanguageNames { get; } = Array.AsReadOnly(new[] { "English (ZA)", "Afrikaans (ZA)", "isiXhosa (ZA)", "isiZulu (ZA)", "Setswana (ZA)" });
    public static CultureInfo Culture => clientCulture ?? CultureInfo.CurrentUICulture;
    public static string Language => Normalize(Culture.Name);
    public static event Action? LanguageChanged;

    /// <summary>Accepts only the five supported locales and otherwise uses South African English.</summary>
    public static string Normalize(string? language) => Languages.FirstOrDefault(value => string.Equals(value, language, StringComparison.OrdinalIgnoreCase)) ?? "en-ZA";

    /// <summary>Changes a client UI without reloading its draft. Servers never call this process-wide client method.</summary>
    public static void SetClientLanguage(string? language)
    {
        clientCulture = CultureInfo.GetCultureInfo(Normalize(language));
        CultureInfo.DefaultThreadCurrentCulture = clientCulture;
        CultureInfo.DefaultThreadCurrentUICulture = clientCulture;
        CultureInfo.CurrentCulture = clientCulture;
        CultureInfo.CurrentUICulture = clientCulture;
        LanguageChanged?.Invoke();
    }

    /// <summary>Looks up a complete English message using a cached key map and the current client/request culture.</summary>
    public static string T(string english) => Keys.TryGetValue(english, out var key) ? Get(key) : english;

    /// <summary>Reads a stable resource key, also used by native XAML bindings.</summary>
    public static string Get(string key) => Resources[Language].GetString(key, CultureInfo.InvariantCulture) ?? key;

    /// <summary>Translates a full sentence before inserting values, keeping user content and references unchanged.</summary>
    public static string F(FormattableString sentence) => string.Format(Culture, T(sentence.Format), sentence.GetArguments());
}
