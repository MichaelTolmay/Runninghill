namespace Runninghill.Maui;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly AppShell shell;

    public App(AppShell shell)
    {
        InitializeComponent();
        // The supplied design is a light theme; native input chrome follows it too.
        UserAppTheme = AppTheme.Light;
        this.shell = shell;
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(shell);
}
