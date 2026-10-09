using System.ComponentModel;
using Runninghill.Contracts;

namespace Runninghill.Maui;

/// <summary>Connects a XAML label to a translated resource without rebuilding the page.</summary>
[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public sealed class TranslateExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = "";

    /// <summary>Uses a typed getter so native AOT does not need reflection to find a translation.</summary>
    public BindingBase ProvideValue(IServiceProvider serviceProvider)
    {
        return Binding.Create(static (TranslationSource source) => source.Value, source: TranslationSource.For(Key));
    }

    /// <summary>Supports the non-generic XAML markup interface.</summary>
    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}

/// <summary>Notifies existing native bindings when the user changes language.</summary>
public sealed class TranslationSource : INotifyPropertyChanged
{
    private static readonly Dictionary<string, TranslationSource> Sources = new(StringComparer.Ordinal);
    private readonly string key;
    public string Value => AppText.Get(key);
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Registers one application-lifetime listener; bindings manage their own weak subscriptions.</summary>
    static TranslationSource() => AppText.LanguageChanged += () =>
    {
        foreach (var source in Sources.Values)
            source.PropertyChanged?.Invoke(source, new PropertyChangedEventArgs(nameof(Value)));
    };

    /// <summary>Keeps only a resource key; it never holds a page or control alive.</summary>
    private TranslationSource(string key) => this.key = key;

    /// <summary>Shares one tiny binding source per resource; native UI access stays on the main thread.</summary>
    public static TranslationSource For(string key)
    {
        if (!Sources.TryGetValue(key, out var source)) Sources[key] = source = new(key);
        return source;
    }
}
