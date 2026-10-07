namespace Runninghill.Maui;

public partial class AppShell : Shell
{
    public AppShell(MainPage mainPage)
    {
        InitializeComponent();
        Items.Add(new ShellContent { Title = "Home", Content = mainPage, Route = "Home" });
    }
}
