namespace Runninghill.Maui;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly AppShell shell;

    public App(AppShell shell)
    {
        InitializeComponent();
        this.shell = shell;
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(shell);
}
