namespace Runninghill.Maui;

/// <summary>
/// Provides native navigation around the word collection page.
/// </summary>
public partial class AppShell : Shell
{
    /// <summary>
    /// Loads the shell and installs the supplied page, preserving its injected logger.
    /// </summary>
    public AppShell(MainPage mainPage)
    {
        InitializeComponent();
        // Supply the already-created page instead of asking XAML to construct it without its logger.
        var home = new ShellContent { Content = mainPage, Route = "Home" };
        home.SetBinding(ShellContent.TitleProperty,
            new TranslateExtension { Key = "Text_70f8bb9a8a53" }.ProvideValue(null!));
        Items.Add(home);
    }
}
