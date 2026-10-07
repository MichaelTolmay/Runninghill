namespace Runninghill.Maui;

public partial class AppShell : Shell
{
    public AppShell(MainPage mainPage)
    {
        InitializeComponent();
        // Supply the already-created page instead of asking XAML to construct it without its logger.
        Items.Add(new ShellContent { Title = "Home", Content = mainPage, Route = "Home" });
    }
}
