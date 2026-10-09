using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Runninghill.Clients;

/// <summary>One company palette. Its filename identifies the company and brightness.</summary>
internal sealed record AppThemeDefinition(string Id, string Company, string Mode, IReadOnlyDictionary<string, string> Colors, string Json);

/// <summary>Loads the same embedded theme files in the web and native clients without reflection-based JSON serialization.</summary>
internal static class AppThemes
{
    private static readonly HashSet<string> Resources = typeof(AppThemes).Assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> IconData = new(StringComparer.Ordinal);
    public static IReadOnlyList<AppThemeDefinition> All { get; } = Load();
    public static bool HasInvalidFiles { get; private set; }

    /// <summary>Rejects unknown saved modes while preserving a user's explicit Light or Dark choice.</summary>
    public static string NormalizeMode(string? mode) => mode is "Light" or "Dark" ? mode : "System";

    /// <summary>Uses a packaged company image, or the default image of the same brightness when none was supplied.</summary>
    public static string IconId(string themeId)
    {
        var theme = All.FirstOrDefault(theme => theme.Id == themeId) ?? All.First(theme => theme.Id == "Runninghill_light");
        return Resources.Contains("Runninghill.Themes." + theme.Id + "_icon.webp")
            ? theme.Id + "_icon"
            : "Runninghill_" + theme.Mode + "_icon";
    }

    /// <summary>Embeds the original small WebP in the browser page; encodes each image once without extra network requests.</summary>
    public static string IconDataUri(string themeId) => IconData.GetOrAdd(IconId(themeId), static id =>
    {
        using var stream = typeof(AppThemes).Assembly.GetManifestResourceStream("Runninghill.Themes." + id + ".webp")!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return "data:image/webp;base64," + Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
    });

    /// <summary>Chooses brightness first, retaining the company when the operating system changes theme.</summary>
    public static AppThemeDefinition Resolve(string? company, string? mode, bool systemDark)
    {
        var dark = NormalizeMode(mode) == "Dark" || NormalizeMode(mode) == "System" && systemDark;
        var brightness = dark ? "dark" : "light";
        return All.FirstOrDefault(theme => theme.Company == company && theme.Mode == brightness)
            ?? All.First(theme => theme.Company == "Runninghill" && theme.Mode == brightness);
    }

    /// <summary>Reads packaged JSON once; skips broken optional company files instead of breaking the application.</summary>
    private static IReadOnlyList<AppThemeDefinition> Load()
    {
        var assembly = typeof(AppThemes).Assembly;
        const string prefix = "Runninghill.Themes.";
        var themes = new List<AppThemeDefinition>();
        // The built-in pair is verified in CI and provides the required colour roles.
        foreach (var mode in new[] { "light", "dark" })
        {
            using var stream = assembly.GetManifestResourceStream(prefix + "Runninghill_" + mode + ".json")!;
            themes.Add(Read("Runninghill_" + mode, stream, null));
        }
        var keys = themes[0].Colors.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)))
        {
            var id = name[prefix.Length..^5];
            if (themes.Any(theme => theme.Id == id)) continue;
            try
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                themes.Add(Read(id, stream, keys));
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException)
            {
                HasInvalidFiles = true;
            }
        }
        // A pair makes System mode predictable: never silently substitute another company at night.
        var incomplete = themes.GroupBy(theme => theme.Company).Where(group => group.Count() != 2).Select(group => group.Key).ToHashSet();
        HasInvalidFiles |= incomplete.Count > 0;
        return Array.AsReadOnly(themes.Where(theme => !incomplete.Contains(theme.Company)).OrderBy(theme => theme.Id, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Accepts plain colour values only; a theme cannot inject CSS, markup, scripts, or remote URLs.</summary>
    internal static AppThemeDefinition Read(string id, Stream stream, ISet<string>? requiredKeys)
    {
        var separator = id.LastIndexOf('_');
        if (separator < 1 || id[(separator + 1)..] is not ("light" or "dark") ||
            id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
            throw new InvalidDataException("Theme filenames must be company_light.json or company_dark.json.");
        using var document = JsonDocument.Parse(stream);
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!document.RootElement.TryGetProperty("colors", out var palette) || palette.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A theme needs a colors object.");
        foreach (var color in palette.EnumerateObject())
        {
            var value = color.Value.GetString();
            if (value is null || value.Length != 7 || value[0] != '#' || value.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF"))
                throw new InvalidDataException("Theme colours must use #RRGGBB.");
            if (!colors.TryAdd(color.Name, value)) throw new InvalidDataException("Duplicate colour role.");
        }
        if (requiredKeys is not null && !requiredKeys.SetEquals(colors.Keys))
            throw new InvalidDataException("Theme colour roles must match the built-in palette.");
        return new(id, id[..separator], id[(separator + 1)..], new ReadOnlyDictionary<string, string>(colors), palette.GetRawText());
    }
}
