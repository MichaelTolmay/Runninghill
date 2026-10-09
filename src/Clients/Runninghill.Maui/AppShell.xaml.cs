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
        Items.Add(new ShellContent { Title = "Home", Content = mainPage, Route = "Home" });
    }
}
